using System;
using System.Collections.Generic;
using System.Windows;
using Dapper;
using PartnerRelationManager.Services;
using PartnerRelationManager.Models;

namespace PartnerRelationManager
{
    public partial class OnboardingWindow : Window
    {
        private List<string> customPartners = new List<string>();

        public OnboardingWindow()
        {
            InitializeComponent();
        }

        private void BtnAddCustomPartner_Click(object sender, RoutedEventArgs e)
        {
            string name = TxtCustomPartner.Text.Trim();
            if (!string.IsNullOrEmpty(name) && !customPartners.Contains(name))
            {
                customPartners.Add(name);
                LstCustomPartners.Items.Add(name);
                TxtCustomPartner.Clear();
            }
        }

        private void BtnSetup_Click(object sender, RoutedEventArgs e)
        {
            var partners = new List<string>();
            if (ChkHp.IsChecked == true) partners.Add("HP");
            if (ChkLenovo.IsChecked == true) partners.Add("Lenovo");
            if (ChkDell.IsChecked == true) partners.Add("Dell");
            partners.AddRange(customPartners);

            if (partners.Count == 0)
            {
                MessageBox.Show("Please select or add at least one partner.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var countries = new List<(string Name, string Code)>();
            if (ChkNo.IsChecked == true) countries.Add(("Norway", "NO"));
            if (ChkSe.IsChecked == true) countries.Add(("Sweden", "SE"));
            if (ChkDk.IsChecked == true) countries.Add(("Denmark", "DK"));
            if (ChkFi.IsChecked == true) countries.Add(("Finland", "FI"));

            if (countries.Count == 0)
            {
                MessageBox.Show("Please select at least one country.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            using (var connection = DatabaseHelper.GetConnection())
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        foreach (var pName in partners)
                        {
                            foreach (var c in countries)
                            {
                                string dbPartnerName = $"{pName} {c.Name}";

                                var existingPartner = connection.QueryFirstOrDefault<Partner>(
                                    "SELECT Id FROM Partners WHERE Name = @Name", 
                                    new { Name = dbPartnerName }, 
                                    transaction: transaction);

                                int partnerId;
                                if (existingPartner != null)
                                {
                                    partnerId = existingPartner.Id;
                                }
                                else
                                {
                                    partnerId = connection.QuerySingle<int>(@"
                                        INSERT INTO Partners (Name, InternalOwner, Category, StrategicImportance, Status, BusinessAreas, CountryCode, CurrentTier, QbrFrequency)
                                        VALUES (@Name, 'August Eriksen', 'OEM', 'Medium', 'Green', 'Hybrid', @CountryCode, 'None', '2-3 times a year');
                                        SELECT last_insert_rowid();",
                                        new { Name = dbPartnerName, CountryCode = c.Code }, 
                                        transaction: transaction);
                                }

                                // Initialize empty KPI rows for 2025
                                connection.Execute(@"
                                    INSERT INTO KPI_Commercial (PartnerId, Period, TargetMet, Comments)
                                    VALUES (@PartnerId, 2025, 'No', '')
                                    ON CONFLICT(PartnerId, Period) DO NOTHING;",
                                    new { PartnerId = partnerId }, 
                                    transaction: transaction);

                                connection.Execute(@"
                                    INSERT INTO KPI_ProgramControl (PartnerId, Period, RebateEligibility, RiskOfDowngrade, Comments)
                                    VALUES (@PartnerId, 2025, 'No', 'No', '')
                                    ON CONFLICT(PartnerId, Period) DO NOTHING;",
                                    new { PartnerId = partnerId }, 
                                    transaction: transaction);
                            }
                        }

                        transaction.Commit();
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        MessageBox.Show($"Error initializing workspace: {ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }
            }

            DialogResult = true;
            Close();
        }
    }
}
