using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Dapper;
using PartnerRelationManager.Services;
using PartnerRelationManager.Models;

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
                int totalCountries = connection.ExecuteScalar<int>("SELECT COUNT(DISTINCT CountryCode) FROM Partners WHERE CountryCode IS NOT NULL AND CountryCode != '';");
                int totalActivities = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM Activities;");

                TxtTotalPartners.Text = totalPartners.ToString();
                TxtTotalCountries.Text = totalCountries.ToString();
                TxtTotalActivities.Text = totalActivities.ToString();

                // Set ToolTips for stat cards
                var partnerNames = connection.Query<string>("SELECT Name FROM Partners ORDER BY Name;").ToList();
                CardTotalPartners.ToolTip = partnerNames.Count > 0 ? string.Join(Environment.NewLine, partnerNames) : "No partners";

                var countryCodes = connection.Query<string>("SELECT DISTINCT CountryCode FROM Partners WHERE CountryCode IS NOT NULL AND CountryCode != '' ORDER BY CountryCode;").ToList();
                CardActiveCountries.ToolTip = countryCodes.Count > 0 ? string.Join(", ", countryCodes) : "No active countries";

                // Update Status Matrix
                var oems = new[] { "Dell", "HP", "Lenovo" };
                var countries = new[] { "NO", "SE", "DK", "FI" };
                var allPartners = connection.Query<Partner>("SELECT Name, Status, CountryCode FROM Partners;").ToList();

                foreach (var oem in oems)
                {
                    foreach (var country in countries)
                    {
                        var match = allPartners.FirstOrDefault(p => 
                            p.CountryCode.Equals(country, StringComparison.OrdinalIgnoreCase) && 
                            p.Name.StartsWith(oem, StringComparison.OrdinalIgnoreCase)
                        );

                        string dotName = $"Dot_{oem}_{country}";
                        var ellipse = this.FindName(dotName) as System.Windows.Shapes.Ellipse;
                        if (ellipse != null)
                        {
                            if (match != null)
                            {
                                string status = match.Status ?? string.Empty;
                                if (status.Equals("Green", StringComparison.OrdinalIgnoreCase))
                                {
                                    ellipse.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(38, 188, 116)); // #26BC74
                                    ellipse.ToolTip = $"{match.Name}: Green";
                                }
                                else if (status.Equals("Amber", StringComparison.OrdinalIgnoreCase))
                                {
                                    ellipse.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 155, 35)); // #F59B23
                                    ellipse.ToolTip = $"{match.Name}: Amber";
                                }
                                else if (status.Equals("Red", StringComparison.OrdinalIgnoreCase))
                                {
                                    ellipse.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(232, 17, 35)); // #E81123
                                    ellipse.ToolTip = $"{match.Name}: Red";
                                }
                                else
                                {
                                    ellipse.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray);
                                    ellipse.ToolTip = $"{match.Name}: No Status";
                                }
                            }
                            else
                            {
                                ellipse.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 220, 220)); // Light Gray
                                ellipse.ToolTip = $"{oem} ({country}): Not Managed";
                            }
                        }
                    }
                }

                // 2. Generate Compliance Warnings
                var warnings = new List<DashboardWarning>();

                // Partner status warnings
                foreach (var p in allPartners)
                {
                    string status = p.Status ?? string.Empty;
                    if (status.Equals("Red", StringComparison.OrdinalIgnoreCase) || 
                        status.Equals("Amber", StringComparison.OrdinalIgnoreCase))
                    {
                        warnings.Add(new DashboardWarning
                        {
                            DisplayTitle = $"Status Alert - {p.Name} ({p.CountryCode})",
                            DisplayDetails = $"Partner status is currently set to {status}.",
                            SeverityColor = status.Equals("Red", StringComparison.OrdinalIgnoreCase) ? "#FFE81123" : "#FFF59B23"
                        });
                    }
                }

                // Program Control warnings
                var programControlList = connection.Query(@"
                    SELECT p.Name as PartnerName, p.CountryCode, k.RiskOfDowngrade, k.Variance
                    FROM KPI_ProgramControl k
                    JOIN Partners p ON k.PartnerId = p.Id"
                ).ToList();

                foreach (var item in programControlList)
                {
                    if (item.RiskOfDowngrade == "Yes" || item.RiskOfDowngrade == "True")
                    {
                        warnings.Add(new DashboardWarning
                        {
                            DisplayTitle = $"Downgrade Risk - {item.PartnerName} ({item.CountryCode})",
                            DisplayDetails = "Program control assessment reports high risk of downgrade.",
                            SeverityColor = "#FFE81123" // Red
                        });
                    }
                    double? varVal = item.Variance;
                    if (varVal.HasValue && Math.Abs(varVal.Value) > 0.10)
                    {
                        warnings.Add(new DashboardWarning
                        {
                            DisplayTitle = $"Revenue Variance Warning - {item.PartnerName} ({item.CountryCode})",
                            DisplayDetails = $"Reported revenue variance between Onitio and OEM is {(varVal.Value * 100):N1}%.",
                            SeverityColor = Math.Abs(varVal.Value) > 0.20 ? "#FFE81123" : "#FFF59B23" // Red or Amber
                        });
                    }
                }

                // 2b. Query KPI_Compliance and generate warnings & pop LstCertifications
                var complianceList = connection.Query(@"
                    SELECT p.Name as PartnerName, p.CountryCode, k.Period, k.CertificationsNeeded, k.RequiredCertifications, k.CertificationsCovered, k.CertsExpiring3Months, k.CertsExpiring6Months, k.CertsExpiring12Months, k.ProgramComplianceStatus, k.TierRisk
                    FROM KPI_Compliance k
                    JOIN Partners p ON k.PartnerId = p.Id"
                ).ToList();

                var certViewModels = new List<CertificationDashboardViewModel>();

                foreach (var item in complianceList)
                {
                    double coveredPercent = item.CertificationsCovered ?? 0.0;
                    if (coveredPercent <= 1.0) coveredPercent *= 100.0;

                    bool isRed = (item.CertsExpiring3Months > 0) || (item.ProgramComplianceStatus == "Deviation");
                    bool isAmber = !isRed && ((item.CertsExpiring6Months > 0) || (coveredPercent < 100.0));

                    if (isRed)
                    {
                        warnings.Add(new DashboardWarning
                        {
                            DisplayTitle = $"Compliance Deviation - {item.PartnerName} ({item.CountryCode})",
                            DisplayDetails = $"Deviation detected. Expiring < 3M: {item.CertsExpiring3Months}. Status: {item.ProgramComplianceStatus}.",
                            SeverityColor = "#FFE81123" // Red
                        });
                    }
                    else if (isAmber)
                    {
                        string details = $"Covered: {coveredPercent:F1}%. Expiring < 6M: {item.CertsExpiring6Months}.";
                        warnings.Add(new DashboardWarning
                        {
                            DisplayTitle = $"Compliance Warning - {item.PartnerName} ({item.CountryCode})",
                            DisplayDetails = details,
                            SeverityColor = "#FFF59B23" // Amber
                        });
                    }

                    string colorHex = "#FF26BC74"; // Green
                    if (isRed) colorHex = "#FFE81123";
                    else if (isAmber) colorHex = "#FFF59B23";

                    string detailsText = $"Covered: {coveredPercent:F0}% of {item.RequiredCertifications} req. (Expiring 3M: {item.CertsExpiring3Months}, 6M: {item.CertsExpiring6Months})";
                    certViewModels.Add(new CertificationDashboardViewModel
                    {
                        PartnerName = $"{item.PartnerName} ({item.CountryCode})",
                        Details = detailsText,
                        Period = item.Period.ToString(),
                        StatusColor = colorHex
                    });
                }

                TxtComplianceWarnings.Text = warnings.Count.ToString();
                LstWarnings.ItemsSource = warnings;
                LstCertifications.ItemsSource = certViewModels;

                // 3. Load Activities
                string localCountry = DatabaseHelper.GetSetting("LocalCountry", "NO");
                var activities = connection.Query(@"
                    SELECT a.*, p.Name as PartnerName 
                    FROM Activities a
                    JOIN Partners p ON a.PartnerId = p.Id
                    WHERE p.CountryCode = @LocalCountry
                    ORDER BY a.ActivityDate DESC
                    LIMIT 8", new { LocalCountry = localCountry }
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
            public string SeverityColor { get; set; } = "#FFE81123";
        }

        public class CertificationDashboardViewModel
        {
            public string PartnerName { get; set; } = string.Empty;
            public string Details { get; set; } = string.Empty;
            public string Period { get; set; } = string.Empty;
            public string StatusColor { get; set; } = "#FF888888";
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
