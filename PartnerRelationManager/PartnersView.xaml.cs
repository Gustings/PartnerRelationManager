using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Dapper;
using PartnerRelationManager.Models;
using PartnerRelationManager.Services;

using Activity = PartnerRelationManager.Models.Activity;

namespace PartnerRelationManager
{
    public partial class PartnersView : UserControl
    {
        private List<Partner> allPartners = new List<Partner>();

        public PartnersView()
        {
            InitializeComponent();
            Loaded += PartnersView_Loaded;
        }

        private void PartnersView_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshAll();
        }

        public void RefreshAll()
        {
            LoadPartners();
            RefreshDataForSelectedPartner();
        }

        private void LoadPartners()
        {
            try
            {
                using var connection = DatabaseHelper.GetConnection();
                connection.Open();
                allPartners = connection.Query<Partner>("SELECT * FROM Partners ORDER BY Name;").ToList();
                FilterPartnersList();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading partners: {ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void FilterPartnersList()
        {
            string filterText = TxtSearchPartner.Text.Trim();
            if (string.IsNullOrEmpty(filterText))
            {
                LstPartners.ItemsSource = allPartners;
            }
            else
            {
                LstPartners.ItemsSource = allPartners
                    .Where(p => p.Name.Contains(filterText, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        private void TxtSearchPartner_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterPartnersList();
        }

        private void LstPartners_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var partner = LstPartners.SelectedItem as Partner;
            if (partner == null)
            {
                CardNoSelection.Visibility = Visibility.Visible;
                GridPartnerDetails.Visibility = Visibility.Collapsed;
                return;
            }

            CardNoSelection.Visibility = Visibility.Collapsed;
            GridPartnerDetails.Visibility = Visibility.Visible;

            TxtDetailPartnerName.Text = partner.Name;
            TxtDetailPartnerOwner.Text = partner.InternalOwner;

            // Load profile fields
            TxtEditOwner.Text = partner.InternalOwner;
            TxtEditCategory.Text = partner.Category;
            TxtEditImportance.Text = partner.StrategicImportance;
            TxtEditStatus.Text = partner.Status;
            TxtEditBusinessAreas.Text = partner.BusinessAreas;
            TxtEditCountryCode.Text = partner.CountryCode;
            TxtEditPartnerProgram.Text = partner.PartnerProgram;
            TxtEditCurrentTier.Text = partner.CurrentTier;
            TxtEditPartnerIdentification.Text = partner.PartnerIdentification;
            TxtEditQbrFrequency.Text = partner.QbrFrequency;
            TxtEditComments.Text = partner.Comments;

            RefreshDataForSelectedPartner();
        }

        private void CboFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshDataForSelectedPartner();
        }

        private void RefreshDataForSelectedPartner()
        {
            var partner = LstPartners.SelectedItem as Partner;
            if (partner == null) return;

            int period = GetSelectedPeriod();

            try
            {
                using var connection = DatabaseHelper.GetConnection();
                connection.Open();

                // 1. Profile Lists (Contacts)
                var contacts = connection.Query<Contact>("SELECT * FROM Contacts WHERE PartnerId = @PartnerId", new { PartnerId = partner.Id }).ToList();
                LstContacts.ItemsSource = contacts;

                // 2. Activities list
                var activities = connection.Query<Activity>("SELECT * FROM Activities WHERE PartnerId = @PartnerId ORDER BY ActivityDate DESC", new { PartnerId = partner.Id }).ToList();
                LstActivities.ItemsSource = activities;

                // 3. Documents list
                var docs = connection.Query<Document>(
                    "SELECT * FROM Documents WHERE PartnerId = @PartnerId AND Period = @Period",
                    new { PartnerId = partner.Id, Period = period }).ToList();
                LstDocuments.ItemsSource = docs;

                // 4. KPIs loading
                LoadCommercialKpis(connection, partner.Id, period);
                LoadProgramControlKpis(connection, partner.Id, period);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading partner details: {ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private int GetSelectedPeriod()
        {
            var item = CboPeriod.SelectedItem as ComboBoxItem;
            if (item == null) return 2025;
            return int.Parse(item.Content.ToString() ?? "2025");
        }

        // ==========================================
        // PROFILE SAVE
        // ==========================================
        private void BtnSaveProfile_Click(object sender, RoutedEventArgs e)
        {
            var partner = LstPartners.SelectedItem as Partner;
            if (partner == null) return;

            try
            {
                using var connection = DatabaseHelper.GetConnection();
                connection.Open();

                connection.Execute(@"
                    UPDATE Partners 
                    SET InternalOwner = @InternalOwner, 
                        Category = @Category, 
                        StrategicImportance = @StrategicImportance, 
                        Status = @Status, 
                        BusinessAreas = @BusinessAreas,
                        CountryCode = @CountryCode,
                        PartnerProgram = @PartnerProgram,
                        CurrentTier = @CurrentTier,
                        PartnerIdentification = @PartnerIdentification,
                        QbrFrequency = @QbrFrequency,
                        Comments = @Comments
                    WHERE Id = @Id;",
                    new
                    {
                        InternalOwner = TxtEditOwner.Text,
                        Category = TxtEditCategory.Text,
                        StrategicImportance = TxtEditImportance.Text,
                        Status = TxtEditStatus.Text,
                        BusinessAreas = TxtEditBusinessAreas.Text,
                        CountryCode = TxtEditCountryCode.Text,
                        PartnerProgram = TxtEditPartnerProgram.Text,
                        CurrentTier = TxtEditCurrentTier.Text,
                        PartnerIdentification = TxtEditPartnerIdentification.Text,
                        QbrFrequency = TxtEditQbrFrequency.Text,
                        Comments = TxtEditComments.Text,
                        Id = partner.Id
                    });

                MessageBox.Show("Profile updated successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                
                // Reload partner list and preserve selection
                int selectedId = partner.Id;
                LoadPartners();
                
                // Reselect partner from the reloaded list
                var reloadedPartner = allPartners.FirstOrDefault(p => p.Id == selectedId);
                if (reloadedPartner != null)
                {
                    LstPartners.SelectedItem = reloadedPartner;
                }

                // Refresh dashboard/main UI
                var mainWin = Window.GetWindow(this) as MainWindow;
                mainWin?.RefreshAll();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error updating profile: {ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ==========================================
        // SUB-ITEMS ACTIONS (Contacts, Activities, Documents)
        // ==========================================
        private void BtnAddContact_Click(object sender, RoutedEventArgs e)
        {
            var partner = LstPartners.SelectedItem as Partner;
            if (partner == null) return;

            string name = TxtNewContactName.Text.Trim();
            string role = TxtNewContactRole.Text.Trim();
            string email = TxtNewContactEmail.Text.Trim();
            string phone = TxtNewContactPhone.Text.Trim();

            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Contact Name is required.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                using var connection = DatabaseHelper.GetConnection();
                connection.Open();
                connection.Execute(@"
                    INSERT INTO Contacts (PartnerId, Name, Role, Email, Phone, Notes)
                    VALUES (@PartnerId, @Name, @Role, @Email, @Phone, '');",
                    new { PartnerId = partner.Id, Name = name, Role = role, Email = email, Phone = phone });

                TxtNewContactName.Clear();
                TxtNewContactRole.Clear();
                TxtNewContactEmail.Clear();
                TxtNewContactPhone.Clear();
                RefreshDataForSelectedPartner();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error adding contact: {ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnAddActivity_Click(object sender, RoutedEventArgs e)
        {
            var partner = LstPartners.SelectedItem as Partner;
            if (partner == null) return;

            string title = TxtNewActTitle.Text.Trim();
            string type = TxtNewActType.Text.Trim();
            string owner = TxtNewActOwner.Text.Trim();
            string desc = TxtNewActDesc.Text.Trim();
            string dateStr = TxtNewActDate.Text.Trim();

            if (string.IsNullOrEmpty(title))
            {
                MessageBox.Show("Activity Title is required.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string formattedDate = string.IsNullOrEmpty(dateStr) ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") : dateStr;

            try
            {
                using var connection = DatabaseHelper.GetConnection();
                connection.Open();
                connection.Execute(@"
                    INSERT INTO Activities (PartnerId, ActivityDate, Type, Title, Description, Owner, Status, DueDate)
                    VALUES (@PartnerId, @ActivityDate, @Type, @Title, @Description, @Owner, 'Open', '');",
                    new { PartnerId = partner.Id, ActivityDate = formattedDate, Type = type, Title = title, Description = desc, Owner = owner });

                TxtNewActTitle.Clear();
                TxtNewActType.Clear();
                TxtNewActOwner.Clear();
                TxtNewActDesc.Clear();
                TxtNewActDate.Clear();
                RefreshDataForSelectedPartner();

                var mainWin = Window.GetWindow(this) as MainWindow;
                mainWin?.RefreshAll();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error adding activity: {ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ==========================================
        // PARTNER CREATION
        // ==========================================
        private void BtnAddPartner_Click(object sender, RoutedEventArgs e)
        {
            string name = TxtNewPartnerName.Text.Trim();
            if (string.IsNullOrWhiteSpace(name)) return;

            try
            {
                using var connection = DatabaseHelper.GetConnection();
                connection.Open();

                var existing = connection.QueryFirstOrDefault<Partner>(
                    "SELECT * FROM Partners WHERE Name = @Name", new { Name = name });
                if (existing != null)
                {
                    MessageBox.Show($"A partner with the name '{name}' already exists.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                int id = connection.QuerySingle<int>(@"
                    INSERT INTO Partners (Name, InternalOwner, Category, StrategicImportance, Status, BusinessAreas, CountryCode)
                    VALUES (@Name, 'August Eriksen', 'Preferred', 'Medium', 'Green', 'Hardware', 'NO');
                    SELECT last_insert_rowid();",
                    new { Name = name });

                TxtNewPartnerName.Clear();
                LoadPartners();
                
                // Select new partner
                var newPartner = allPartners.FirstOrDefault(p => p.Id == id);
                if (newPartner != null)
                {
                    LstPartners.SelectedItem = newPartner;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error adding partner: {ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ==========================================
        // DOCUMENT MANAGEMENT
        // ==========================================
        private void BtnUploadDoc_Click(object sender, RoutedEventArgs e)
        {
            var partner = LstPartners.SelectedItem as Partner;
            if (partner == null) return;
            int period = GetSelectedPeriod();

            string assetType = TxtDocAssetType.Text.Trim();
            if (string.IsNullOrEmpty(assetType)) assetType = "General Asset";

            var openFileDialog = new OpenFileDialog
            {
                Title = "Select File to Link/Upload"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    string sourcePath = openFileDialog.FileName;
                    string fileName = Path.GetFileName(sourcePath);
                    string destPath = Path.Combine(DatabaseHelper.DocumentsFolder, $"{partner.Id}_{period}_{fileName}");

                    // Copy file to local storage
                    File.Copy(sourcePath, destPath, overwrite: true);

                    using var connection = DatabaseHelper.GetConnection();
                    connection.Open();
                    connection.Execute(@"
                        INSERT INTO Documents (PartnerId, Period, FileName, FilePath, UploadDate, AssetType)
                        VALUES (@PartnerId, @Period, @FileName, @FilePath, @UploadDate, @AssetType);",
                        new
                        {
                            PartnerId = partner.Id,
                            Period = period,
                            FileName = fileName,
                            FilePath = destPath,
                            UploadDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                            AssetType = assetType
                        });

                    TxtDocAssetType.Clear();
                    RefreshDataForSelectedPartner();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error linking document: {ex.Message}", "Upload Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnOpenDoc_Click(object sender, RoutedEventArgs e)
        {
            OpenSelectedDocument();
        }

        private void LstDocuments_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            OpenSelectedDocument();
        }

        private void OpenSelectedDocument()
        {
            var doc = LstDocuments.SelectedItem as Document;
            if (doc == null) return;

            if (File.Exists(doc.FilePath))
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = doc.FilePath,
                        UseShellExecute = true
                    };
                    Process.Start(psi);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not open document: {ex.Message}", "File Open Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show("The physical file could not be found. It may have been deleted or moved.", "File Missing", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ==========================================
        // KPI FORMS BINDING & PERSISTENCE
        // ==========================================
        private void LoadCommercialKpis(System.Data.IDbConnection conn, int partnerId, int period)
        {
            var kpi = conn.QueryFirstOrDefault<KPI_Commercial>(
                "SELECT * FROM KPI_Commercial WHERE PartnerId = @PartnerId AND Period = @Period",
                new { PartnerId = partnerId, Period = period });

            TxtArr.Text = kpi?.AnnualRecurringRevenue?.ToString() ?? "";
            TxtUpfront.Text = kpi?.UpfrontRevenue?.ToString() ?? "";
            TxtOemAttach.Text = kpi?.OemServiceAttachRate?.ToString() ?? "";
            TxtOnitioAttach.Text = kpi?.OnitioServiceAttachRate?.ToString() ?? "";
            TxtLifecycleMargin.Text = kpi?.LifecycleMargin?.ToString() ?? "";
            SetComboValue(CboCommercialTargetMet, kpi?.TargetMet);
            TxtCommercialComments.Text = kpi?.Comments ?? "";
        }

        private void BtnSaveCommercial_Click(object sender, RoutedEventArgs e)
        {
            var partner = LstPartners.SelectedItem as Partner;
            if (partner == null) return;
            int period = GetSelectedPeriod();

            decimal? arr = ParseDecimal(TxtArr.Text);
            decimal? upfront = ParseDecimal(TxtUpfront.Text);
            double? oem = ParseDouble(TxtOemAttach.Text);
            double? onitio = ParseDouble(TxtOnitioAttach.Text);
            double? margin = ParseDouble(TxtLifecycleMargin.Text);
            string targetMet = (CboCommercialTargetMet.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "No";

            try
            {
                using var connection = DatabaseHelper.GetConnection();
                connection.Open();
                connection.Execute(@"
                    INSERT INTO KPI_Commercial (PartnerId, Period, AnnualRecurringRevenue, UpfrontRevenue, OemServiceAttachRate, OnitioServiceAttachRate, LifecycleMargin, TargetMet, Comments)
                    VALUES (@PartnerId, @Period, @AnnualRecurringRevenue, @UpfrontRevenue, @OemServiceAttachRate, @OnitioServiceAttachRate, @LifecycleMargin, @TargetMet, @Comments)
                    ON CONFLICT(PartnerId, Period) DO UPDATE SET
                        AnnualRecurringRevenue=@AnnualRecurringRevenue, UpfrontRevenue=@UpfrontRevenue, OemServiceAttachRate=@OemServiceAttachRate,
                        OnitioServiceAttachRate=@OnitioServiceAttachRate, LifecycleMargin=@LifecycleMargin, TargetMet=@TargetMet, Comments=@Comments;",
                    new
                    {
                        PartnerId = partner.Id,
                        Period = period,
                        AnnualRecurringRevenue = arr,
                        UpfrontRevenue = upfront,
                        OemServiceAttachRate = oem,
                        OnitioServiceAttachRate = onitio,
                        LifecycleMargin = margin,
                        TargetMet = targetMet,
                        Comments = TxtCommercialComments.Text
                    });

                MessageBox.Show("Commercial KPIs saved!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving Commercial KPIs: {ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadProgramControlKpis(System.Data.IDbConnection conn, int partnerId, int period)
        {
            var kpi = conn.QueryFirstOrDefault<KPI_ProgramControl>(
                "SELECT * FROM KPI_ProgramControl WHERE PartnerId = @PartnerId AND Period = @Period",
                new { PartnerId = partnerId, Period = period });

            TxtProgOnitioRev.Text = kpi?.ReportedRevenueOnitio?.ToString() ?? "";
            TxtProgOemRev.Text = kpi?.ReportedRevenueOem?.ToString() ?? "";
            TxtProgVariance.Text = kpi?.Variance?.ToString() ?? "";
            SetComboValue(CboProgRebate, kpi?.RebateEligibility);
            TxtProgTierProgress.Text = kpi?.TierProgress?.ToString() ?? "";
            SetComboValue(CboProgRisk, kpi?.RiskOfDowngrade);
            TxtProgComments.Text = kpi?.Comments ?? "";
        }

        private void BtnSaveProgramControl_Click(object sender, RoutedEventArgs e)
        {
            var partner = LstPartners.SelectedItem as Partner;
            if (partner == null) return;
            int period = GetSelectedPeriod();

            decimal? onitio = ParseDecimal(TxtProgOnitioRev.Text);
            decimal? oem = ParseDecimal(TxtProgOemRev.Text);
            double? varVal = ParseDouble(TxtProgVariance.Text);
            string rebate = (CboProgRebate.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "No";
            double? progress = ParseDouble(TxtProgTierProgress.Text);
            string risk = (CboProgRisk.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "No";

            try
            {
                using var connection = DatabaseHelper.GetConnection();
                connection.Open();
                connection.Execute(@"
                    INSERT INTO KPI_ProgramControl (PartnerId, Period, ReportedRevenueOnitio, ReportedRevenueOem, Variance, RebateEligibility, TierProgress, RiskOfDowngrade, Comments)
                    VALUES (@PartnerId, @Period, @ReportedRevenueOnitio, @ReportedRevenueOem, @Variance, @RebateEligibility, @TierProgress, @RiskOfDowngrade, @Comments)
                    ON CONFLICT(PartnerId, Period) DO UPDATE SET
                        ReportedRevenueOnitio=@ReportedRevenueOnitio, ReportedRevenueOem=@ReportedRevenueOem, Variance=@Variance,
                        RebateEligibility=@RebateEligibility, TierProgress=@TierProgress, RiskOfDowngrade=@RiskOfDowngrade, Comments=@Comments;",
                    new
                    {
                        PartnerId = partner.Id,
                        Period = period,
                        ReportedRevenueOnitio = onitio,
                        ReportedRevenueOem = oem,
                        Variance = varVal,
                        RebateEligibility = rebate,
                        TierProgress = progress,
                        RiskOfDowngrade = risk,
                        Comments = TxtProgComments.Text
                    });

                MessageBox.Show("Program Control KPIs saved!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                
                var mainWin = Window.GetWindow(this) as MainWindow;
                mainWin?.RefreshAll();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving Program Control KPIs: {ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ==========================================
        // HELPERS
        // ==========================================
        private void SetComboValue(ComboBox cbo, string val)
        {
            if (val == null)
            {
                cbo.SelectedIndex = -1;
                return;
            }

            for (int i = 0; i < cbo.Items.Count; i++)
            {
                var item = cbo.Items[i] as ComboBoxItem;
                if (item != null && string.Equals(item.Content?.ToString(), val, StringComparison.OrdinalIgnoreCase))
                {
                    cbo.SelectedIndex = i;
                    return;
                }
            }
            cbo.SelectedIndex = -1;
        }

        private decimal? ParseDecimal(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string clean = text.Replace(",", ".").Trim();
            if (decimal.TryParse(clean, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal val)) return val;
            return null;
        }

        private double? ParseDouble(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string clean = text.Replace(",", ".").Trim();
            if (double.TryParse(clean, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val)) return val;
            return null;
        }
    }
}