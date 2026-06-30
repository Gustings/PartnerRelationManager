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
                        // Ensure Tiers exist (from seeding, but check and retrieve None tier id)
                        var tierNone = connection.QueryFirstOrDefault<Tier>(
                            "SELECT Id FROM Tiers WHERE Name = 'None'", transaction: transaction);
                        int defaultTierId = tierNone?.Id ?? 1;

                        // Insert selected countries and retrieve/ensure IDs
                        var countryIds = new List<int>();
                        foreach (var c in countries)
                        {
                            var existing = connection.QueryFirstOrDefault<Country>(
                                "SELECT Id FROM Countries WHERE Code = @Code", new { Code = c.Code }, transaction: transaction);
                            if (existing != null)
                            {
                                countryIds.Add(existing.Id);
                            }
                            else
                            {
                                int id = connection.QuerySingle<int>(
                                    "INSERT INTO Countries (Name, Code) VALUES (@Name, @Code); SELECT last_insert_rowid();",
                                    new { Name = c.Name, Code = c.Code }, transaction: transaction);
                                countryIds.Add(id);
                            }
                        }

                        // Insert selected partners and partner tiers
                        foreach (var pName in partners)
                        {
                            var existingPartner = connection.QueryFirstOrDefault<Partner>(
                                "SELECT Id FROM Partners WHERE Name = @Name", new { Name = pName }, transaction: transaction);

                            int partnerId;
                            if (existingPartner != null)
                            {
                                partnerId = existingPartner.Id;
                            }
                            else
                            {
                                partnerId = connection.QuerySingle<int>(@"
                                    INSERT INTO Partners (Name, InternalOwner, Category, StrategicImportance, Status, BusinessAreas)
                                    VALUES (@Name, 'August Eriksen', 'OEM', 'Medium', 'Green', 'Hybrid');
                                    SELECT last_insert_rowid();",
                                    new { Name = pName }, transaction: transaction);
                            }

                            // Initialize partner tier for 2025 across all selected countries
                            foreach (int cId in countryIds)
                            {
                                connection.Execute(@"
                                    INSERT INTO PartnerTiers (PartnerId, CountryId, Period, TierId)
                                    VALUES (@PartnerId, @CountryId, 2025, @TierId)
                                    ON CONFLICT(PartnerId, CountryId, Period) DO NOTHING;",
                                    new { PartnerId = partnerId, CountryId = cId, TierId = defaultTierId }, transaction: transaction);

                                // Initialize empty KPI rows so they exist and can be easily edited
                                connection.Execute(@"
                                    INSERT INTO KPI_Commercial (PartnerId, CountryId, Period, TargetMet, Comments)
                                    VALUES (@PartnerId, @CountryId, 2025, 'No', '')
                                    ON CONFLICT(PartnerId, CountryId, Period) DO NOTHING;",
                                    new { PartnerId = partnerId, CountryId = cId }, transaction: transaction);

                                connection.Execute(@"
                                    INSERT INTO KPI_Compliance (PartnerId, CountryId, Period, CertificationsNeeded, ProgramComplianceStatus, TierRisk, Comments)
                                    VALUES (@PartnerId, @CountryId, 2025, 'Yes', 'OK', 'No', '')
                                    ON CONFLICT(PartnerId, CountryId, Period) DO NOTHING;",
                                    new { PartnerId = partnerId, CountryId = cId }, transaction: transaction);

                                connection.Execute(@"
                                    INSERT INTO KPI_ProgramControl (PartnerId, CountryId, Period, RebateEligibility, RiskOfDowngrade, Comments)
                                    VALUES (@PartnerId, @CountryId, 2025, 'No', 'No', '')
                                    ON CONFLICT(PartnerId, CountryId, Period) DO NOTHING;",
                                    new { PartnerId = partnerId, CountryId = cId }, transaction: transaction);

                                connection.Execute(@"
                                    INSERT INTO KPI_SustainabilityESG (PartnerId, CountryId, Period, TakeBackRecyclingProgram, RefurbSecondLifeSupport, EnvironmentalDataAvailable, EsgComplianceStatus, LogisticsEmissionReduction, SustainabilityQualificationMet, Comments)
                                    VALUES (@PartnerId, @CountryId, 2025, 'No', 'No', 'No', 'OK', 'No', 'No', '')
                                    ON CONFLICT(PartnerId, CountryId, Period) DO NOTHING;",
                                    new { PartnerId = partnerId, CountryId = cId }, transaction: transaction);
                            }

                            // Initialize partner-wide KPIs for 2025
                            connection.Execute(@"
                                INSERT INTO KPI_Operational (PartnerId, Period, TargetMet, Comments)
                                VALUES (@PartnerId, 2025, 'No', '')
                                ON CONFLICT(PartnerId, Period) DO NOTHING;",
                                new { PartnerId = partnerId }, transaction: transaction);

                            connection.Execute(@"
                                INSERT INTO KPI_Strategic (PartnerId, Period, DegreeOfStandardization, StrategicFitAssessment, Comments)
                                VALUES (@PartnerId, 2025, 'Medium', '', '')
                                ON CONFLICT(PartnerId, Period) DO NOTHING;",
                                new { PartnerId = partnerId }, transaction: transaction);
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
