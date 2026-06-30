using System;
using System.Collections.Generic;
using System.Windows;
using Dapper;
using PartnerRelationManager.Services;
using PartnerRelationManager.Models;

namespace PartnerRelationManager
{
    public partial class AddPartnerWindow : Window
    {
        public AddPartnerWindow()
        {
            InitializeComponent();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnCreate_Click(object sender, RoutedEventArgs e)
        {
            string oemName = TxtOemName.Text.Trim();
            if (string.IsNullOrEmpty(oemName))
            {
                MessageBox.Show("Please enter an OEM name.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
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

            try
            {
                using var connection = DatabaseHelper.GetConnection();
                connection.Open();
                using var transaction = connection.BeginTransaction();
                try
                {
                    foreach (var c in countries)
                    {
                        string dbPartnerName = $"{oemName} {c.Name}";

                        var existingPartner = connection.QueryFirstOrDefault<Partner>(
                            "SELECT Id FROM Partners WHERE Name = @Name", 
                            new { Name = dbPartnerName }, 
                            transaction: transaction);

                        if (existingPartner != null)
                        {
                            MessageBox.Show($"Partner '{dbPartnerName}' already exists.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }

                        int partnerId = connection.QuerySingle<int>(@"
                            INSERT INTO Partners (Name, InternalOwner, Category, StrategicImportance, Status, BusinessAreas, CountryCode, CurrentTier, QbrFrequency)
                            VALUES (@Name, 'August Eriksen', 'Preferred', 'Medium', 'Green', 'Hardware', @CountryCode, 'None', '2-3 times a year');
                            SELECT last_insert_rowid();",
                            new { Name = dbPartnerName, CountryCode = c.Code }, 
                            transaction: transaction);

                            // Initialize empty KPI rows for 2025, 2026, 2027
                            foreach (int period in new[] { 2025, 2026, 2027 })
                            {
                                connection.Execute(@"
                                    INSERT INTO KPI_Commercial (PartnerId, Period, TargetMet, Comments)
                                    VALUES (@PartnerId, @Period, 'No', '')
                                    ON CONFLICT(PartnerId, Period) DO NOTHING;",
                                    new { PartnerId = partnerId, Period = period }, 
                                    transaction: transaction);

                                connection.Execute(@"
                                    INSERT INTO KPI_ProgramControl (PartnerId, Period, RebateEligibility, RiskOfDowngrade, Comments)
                                    VALUES (@PartnerId, @Period, 'No', 'No', '')
                                    ON CONFLICT(PartnerId, Period) DO NOTHING;",
                                    new { PartnerId = partnerId, Period = period }, 
                                    transaction: transaction);

                                connection.Execute(@"
                                    INSERT INTO KPI_Compliance (PartnerId, Period, CertificationsNeeded, RequiredCertifications, CertificationsCovered, CertsExpiring3Months, CertsExpiring6Months, CertsExpiring12Months, ProgramComplianceStatus, TierRisk, Comments)
                                    VALUES (@PartnerId, @Period, 'No', 0, 0.0, 0, 0, 0, 'OK', 'No', '')
                                    ON CONFLICT(PartnerId, Period) DO NOTHING;",
                                    new { PartnerId = partnerId, Period = period }, 
                                    transaction: transaction);
                            }
                    }

                    transaction.Commit();
                    DialogResult = true;
                    Close();
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    MessageBox.Show($"Error creating partner: {ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Database connection error: {ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
