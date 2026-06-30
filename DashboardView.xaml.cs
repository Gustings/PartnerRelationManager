using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Dapper;
using PartnerRelationManager.Services;

namespace PartnerRelationManager
{
    public partial class DashboardView : UserControl
    {
        public DashboardView()
        {
            InitializeComponent();
            Loaded += DashboardView_Loaded;
        }

        private void DashboardView_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshDashboard();
        }

        public void RefreshDashboard()
        {
            try
            {
                using var connection = DatabaseHelper.GetConnection();
                connection.Open();

                // 1. Get quick stats
                int totalPartners = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM Partners;");
                int totalCountries = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM Countries;");
                int totalActivities = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM Activities;");

                TxtTotalPartners.Text = totalPartners.ToString();
                TxtTotalCountries.Text = totalCountries.ToString();
                TxtTotalActivities.Text = totalActivities.ToString();

                // 2. Generate Compliance Warnings
                var warnings = new List<DashboardWarning>();

                // Compliance sheet warnings
                var complianceList = connection.Query(@"
                    SELECT p.Name as PartnerName, c.Code as CountryCode, k.TierRisk, k.ProgramComplianceStatus, k.CertsExpiring3Months
                    FROM KPI_Compliance k
                    JOIN Partners p ON k.PartnerId = p.Id
                    JOIN Countries c ON k.CountryId = c.Id"
                ).ToList();

                foreach (var item in complianceList)
                {
                    if (item.TierRisk == "Yes" || item.TierRisk == "True")
                    {
                        warnings.Add(new DashboardWarning
                        {
                            DisplayTitle = $"Tier Risk Alert - {item.PartnerName} ({item.CountryCode})",
                            DisplayDetails = "Partner has a flag indicating risk of tier downgrade due to compliance issues."
                        });
                    }
                    if (item.ProgramComplianceStatus == "Deviation")
                    {
                        warnings.Add(new DashboardWarning
                        {
                            DisplayTitle = $"Compliance Deviation - {item.PartnerName} ({item.CountryCode})",
                            DisplayDetails = "Official program status is set to Deviation."
                        });
                    }
                    if (item.CertsExpiring3Months > 0)
                    {
                        warnings.Add(new DashboardWarning
                        {
                            DisplayTitle = $"Expiring Certifications - {item.PartnerName} ({item.CountryCode})",
                            DisplayDetails = $"{item.CertsExpiring3Months} certifications are expiring in less than 3 months."
                        });
                    }
                }

                // Program Control warnings
                var programControlList = connection.Query(@"
                    SELECT p.Name as PartnerName, c.Code as CountryCode, k.RiskOfDowngrade, k.Variance
                    FROM KPI_ProgramControl k
                    JOIN Partners p ON k.PartnerId = p.Id
                    JOIN Countries c ON k.CountryId = c.Id"
                ).ToList();

                foreach (var item in programControlList)
                {
                    if (item.RiskOfDowngrade == "Yes" || item.RiskOfDowngrade == "True")
                    {
                        warnings.Add(new DashboardWarning
                        {
                            DisplayTitle = $"Downgrade Risk - {item.PartnerName} ({item.CountryCode})",
                            DisplayDetails = "Program control assessment reports high risk of downgrade."
                        });
                    }
                    double? varVal = item.Variance;
                    if (varVal.HasValue && Math.Abs(varVal.Value) > 0.10)
                    {
                        warnings.Add(new DashboardWarning
                        {
                            DisplayTitle = $"Revenue Variance Warning - {item.PartnerName} ({item.CountryCode})",
                            DisplayDetails = $"Reported revenue variance between Onitio and OEM is {(varVal.Value * 100):N1}%."
                        });
                    }
                }

                TxtComplianceWarnings.Text = warnings.Count.ToString();
                LstWarnings.ItemsSource = warnings;

                // 3. Load Activities
                var activities = connection.Query(@"
                    SELECT a.*, p.Name as PartnerName 
                    FROM Activities a
                    JOIN Partners p ON a.PartnerId = p.Id
                    ORDER BY a.ActivityDate DESC
                    LIMIT 8"
                ).Select(row => new ActivityDashboardViewModel
                {
                    Title = row.Title,
                    PartnerName = row.PartnerName,
                    DisplayDate = FormatDate(row.ActivityDate),
                    Status = $"Owner: {row.Owner} | Status: {row.Status}"
                }).ToList();

                LstActivities.ItemsSource = activities;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading dashboard: {ex.Message}", "Dashboard Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string FormatDate(string dateStr)
        {
            if (string.IsNullOrWhiteSpace(dateStr)) return "TBD";
            if (DateTime.TryParse(dateStr, out DateTime dt))
            {
                return dt.ToString("yyyy-MM-dd");
            }
            return dateStr;
        }

        public class DashboardWarning
        {
            public string DisplayTitle { get; set; } = string.Empty;
            public string DisplayDetails { get; set; } = string.Empty;
        }

        public class ActivityDashboardViewModel
        {
            public string Title { get; set; } = string.Empty;
            public string PartnerName { get; set; } = string.Empty;
            public string DisplayDate { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
        }
    }
}
