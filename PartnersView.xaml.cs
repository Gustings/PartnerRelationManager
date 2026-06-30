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
        private List<Country> allCountries = new List<Country>();

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
            LoadCountries();
            LoadPartners();
            RefreshDataForSelectedPartner();
        }

        private void LoadCountries()
        {
            try
            {
                using var connection = DatabaseHelper.GetConnection();
                connection.Open();
                allCountries = connection.Query<Country>("SELECT * FROM Countries ORDER BY Code;").ToList();
                
                var previousSelection = CboCountry.SelectedValue;
                CboCountry.ItemsSource = allCountries;
                
                if (allCountries.Count > 0)
                {
                    if (previousSelection != null && allCountries.Any(c => c.Id == (int)previousSelection))
                    {
                        CboCountry.SelectedValue = previousSelection;
                    }
                    else
                    {
                        CboCountry.SelectedIndex = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading countries: {ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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

            // Load profile text boxes
            TxtEditOwner.Text = partner.InternalOwner;
            TxtEditCategory.Text = partner.Category;
            TxtEditImportance.Text = partner.StrategicImportance;
            TxtEditStatus.Text = partner.Status;
            TxtEditBusinessAreas.Text = partner.BusinessAreas;

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

            var country = CboCountry.SelectedItem as Country;
            if (country == null) return;

            int period = GetSelectedPeriod();

            try
            {
                using var connection = DatabaseHelper.GetConnection();
                connection.Open();

                // 1. Profile Lists (Contacts & Products)
                var contacts = connection.Query<Contact>("SELECT * FROM Contacts WHERE PartnerId = @PartnerId", new { PartnerId = partner.Id }).ToList();
                LstContacts.ItemsSource = contacts;

                var products = connection.Query<ProductService>("SELECT * FROM ProductsServices WHERE PartnerId = @PartnerId", new { PartnerId = partner.Id }).ToList();
                LstProducts.ItemsSource = products;

                // 2. Activities list
                var activities = connection.Query<Activity>("SELECT * FROM Activities WHERE PartnerId = @PartnerId ORDER BY ActivityDate DESC", new { PartnerId = partner.Id }).ToList();
                LstActivities.ItemsSource = activities;

                // 3. Campaigns & Cases lists
                var campaigns = connection.Query<MarketingCampaign>("SELECT * FROM MarketingCampaigns WHERE PartnerId = @PartnerId", new { PartnerId = partner.Id }).ToList();
                LstCampaigns.ItemsSource = campaigns;

                var cases = connection.Query<CustomerCase>("SELECT * FROM CustomerCases WHERE PartnerId = @PartnerId", new { PartnerId = partner.Id }).ToList();
                LstCases.ItemsSource = cases;

                // 4. Documents list
                var docs = connection.Query<Document>(
                    "SELECT * FROM Documents WHERE PartnerId = @PartnerId AND CountryId = @CountryId AND Period = @Period",
                    new { PartnerId = partner.Id, CountryId = country.Id, Period = period }).ToList();
                LstDocuments.ItemsSource = docs;

                // 5. KPIs loading
                LoadCommercialKpis(connection, partner.Id, country.Id, period);
                LoadComplianceKpis(connection, partner.Id, country.Id, period);
                LoadProgramControlKpis(connection, partner.Id, country.Id, period);
                LoadEsgKpis(connection, partner.Id, country.Id, period);
                LoadOperationalKpis(connection, partner.Id, period);
                LoadStrategicKpis(connection, partner.Id, period);
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
                        BusinessAreas = @BusinessAreas 
                    WHERE Id = @Id;",
                    new
                    {
                        InternalOwner = TxtEditOwner.Text,
                        Category = TxtEditCategory.Text,
                        StrategicImportance = TxtEditImportance.Text,
                        Status = TxtEditStatus.Text,
                        BusinessAreas = TxtEditBusinessAreas.Text,
                        Id = partner.Id
                    });

                MessageBox.Show("Profile updated successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                
                // Reload partner list and preserve selection
                int selectedId = partner.Id;
                LoadPartners();
                LstPartners.SelectedValue = selectedId;
                
                // Refresh dashboard if it's open
                var mainWin = Window.GetWindow(this) as MainWindow;
                mainWin?.RefreshAll();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error updating profile: {ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ==========================================
        // SUB-ITEMS ACTIONS (Contacts, Products, Campaigns, Cases, Activities)
        // ==========================================
        private void BtnAddContact_Click(object sender, RoutedEventArgs e)
        {
            var partner = LstPartners.SelectedItem as Partner;
            if (partner == null) return;

            string name = TxtNewContactName.Text.Trim();
            string role = TxtNewContactRole.Text.Trim();
            string email = TxtNewContactEmail.Text.Trim();

            if (string.IsNullOrEmpty(name)) return;

            try
            {
                using var connection = DatabaseHelper.GetConnection();
                connection.Open();
                connection.Execute(@"
                    INSERT INTO Contacts (PartnerId, Name, Role, Email, Phone, Notes)
                    VALUES (@PartnerId, @Name, @Role, @Email, '', '');",
                    new { PartnerId = partner.Id, Name = name, Role = role, Email = email });

                TxtNewContactName.Clear();
                TxtNewContactRole.Clear();
                TxtNewContactEmail.Clear();
                RefreshDataForSelectedPartner();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error adding contact: {ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnAddProduct_Click(object sender, RoutedEventArgs e)
        {
            var partner = LstPartners.SelectedItem as Partner;
            if (partner == null) return;

            string name = TxtNewProdName.Text.Trim();
            string type = TxtNewProdType.Text.Trim();
            string status = TxtNewProdStatus.Text.Trim();

            if (string.IsNullOrEmpty(name)) return;

            try
            {
                using var connection = DatabaseHelper.GetConnection();
                connection.Open();
                connection.Execute(@"
                    INSERT INTO ProductsServices (PartnerId, Name, Type, Status, Replacement, Description)
                    VALUES (@PartnerId, @Name, @Type, @Status, '', '');",
                    new { PartnerId = partner.Id, Name = name, Type = type, Status = status });

                TxtNewProdName.Clear();
                TxtNewProdType.Clear();
                TxtNewProdStatus.Clear();
                RefreshDataForSelectedPartner();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error adding product: {ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

        private void BtnAddCampaign_Click(object sender, RoutedEventArgs e)
        {
            var partner = LstPartners.SelectedItem as Partner;
            if (partner == null) return;

            string name = TxtNewCampName.Text.Trim();
            string budgetStr = TxtNewCampBudget.Text.Trim();
            string status = TxtNewCampStatus.Text.Trim();

            if (string.IsNullOrEmpty(name)) return;
            decimal.TryParse(budgetStr, out decimal budget);

            try
            {
                using var connection = DatabaseHelper.GetConnection();
                connection.Open();
                connection.Execute(@"
                    INSERT INTO MarketingCampaigns (PartnerId, Name, Type, FundingType, Budget, StartDate, Status, Comments)
                    VALUES (@PartnerId, @Name, 'Standard', 'Joint', @Budget, NULL, @Status, '');",
                    new { PartnerId = partner.Id, Name = name, Budget = budget, Status = status });

                TxtNewCampName.Clear();
                TxtNewCampBudget.Clear();
                TxtNewCampStatus.Clear();
                RefreshDataForSelectedPartner();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error adding campaign: {ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnAddCase_Click(object sender, RoutedEventArgs e)
        {
            var partner = LstPartners.SelectedItem as Partner;
            if (partner == null) return;

            string customer = TxtNewCaseCust.Text.Trim();
            string owner = TxtNewCaseOwner.Text.Trim();
            string status = TxtNewCaseStatus.Text.Trim();

            if (string.IsNullOrEmpty(customer)) return;

            try
            {
                using var connection = DatabaseHelper.GetConnection();
                connection.Open();
                connection.Execute(@"
                    INSERT INTO CustomerCases (PartnerId, CustomerName, ApprovalStatus, IsExternal, Owner, ApprovedText, Comments)
                    VALUES (@PartnerId, @CustomerName, @ApprovalStatus, 1, @Owner, '', '');",
                    new { PartnerId = partner.Id, CustomerName = customer, ApprovalStatus = status, Owner = owner });

                TxtNewCaseCust.Clear();
                TxtNewCaseOwner.Clear();
                TxtNewCaseStatus.Clear();
                RefreshDataForSelectedPartner();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error adding customer case: {ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    INSERT INTO Partners (Name, InternalOwner, Category, StrategicImportance, Status, BusinessAreas)
                    VALUES (@Name, 'August Eriksen', 'Preferred', 'Medium', 'Green', 'Hardware');
                    SELECT last_insert_rowid();",
                    new { Name = name });

                TxtNewPartnerName.Clear();
                LoadPartners();
                
                // Select new partner
                LstPartners.SelectedValue = id;
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
            var country = CboCountry.SelectedItem as Country;
            if (country == null) return;
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
                    string destPath = Path.Combine(DatabaseHelper.DocumentsFolder, $"{partner.Id}_{country.Code}_{period}_{fileName}");

                    // Copy file to local srm storage
                    File.Copy(sourcePath, destPath, overwrite: true);

                    using var connection = DatabaseHelper.GetConnection();
                    connection.Open();
                    connection.Execute(@"
                        INSERT INTO Documents (PartnerId, CountryId, Period, FileName, FilePath, UploadDate, AssetType)
                        VALUES (@PartnerId, @CountryId, @Period, @FileName, @FilePath, @UploadDate, @AssetType);",
                        new
                        {
                            PartnerId = partner.Id,
                            CountryId = country.Id,
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
        private void LoadCommercialKpis(System.Data.IDbConnection conn, int partnerId, int countryId, int period)
        {
            var kpi = conn.QueryFirstOrDefault<KPI_Commercial>(
                "SELECT * FROM KPI_Commercial WHERE PartnerId = @PartnerId AND CountryId = @CountryId AND Period = @Period",
                new { PartnerId = partnerId, CountryId = countryId, Period = period });

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
            var country = CboCountry.SelectedItem as Country;
            if (partner == null || country == null) return;
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
                    INSERT INTO KPI_Commercial (PartnerId, CountryId, Period, AnnualRecurringRevenue, UpfrontRevenue, OemServiceAttachRate, OnitioServiceAttachRate, LifecycleMargin, TargetMet, Comments)
                    VALUES (@PartnerId, @CountryId, @Period, @AnnualRecurringRevenue, @UpfrontRevenue, @OemServiceAttachRate, @OnitioServiceAttachRate, @LifecycleMargin, @TargetMet, @Comments)
                    ON CONFLICT(PartnerId, CountryId, Period) DO UPDATE SET
                        AnnualRecurringRevenue=@AnnualRecurringRevenue, UpfrontRevenue=@UpfrontRevenue, OemServiceAttachRate=@OemServiceAttachRate,
                        OnitioServiceAttachRate=@OnitioServiceAttachRate, LifecycleMargin=@LifecycleMargin, TargetMet=@TargetMet, Comments=@Comments;",
                    new
                    {
                        PartnerId = partner.Id,
                        CountryId = country.Id,
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

        private void LoadComplianceKpis(System.Data.IDbConnection conn, int partnerId, int countryId, int period)
        {
            var kpi = conn.QueryFirstOrDefault<KPI_Compliance>(
                "SELECT * FROM KPI_Compliance WHERE PartnerId = @PartnerId AND CountryId = @CountryId AND Period = @Period",
                new { PartnerId = partnerId, CountryId = countryId, Period = period });

            SetComboValue(CboCertsNeeded, kpi?.CertificationsNeeded);
            TxtRequiredCerts.Text = kpi?.RequiredCertifications?.ToString() ?? "";
            TxtCertsCovered.Text = kpi?.CertificationsCovered?.ToString() ?? "";
            TxtCertsExp3.Text = kpi?.CertsExpiring3Months?.ToString() ?? "";
            TxtCertsExp6.Text = kpi?.CertsExpiring6Months?.ToString() ?? "";
            TxtCertsExp12.Text = kpi?.CertsExpiring12Months?.ToString() ?? "";
            SetComboValue(CboComplianceStatus, kpi?.ProgramComplianceStatus);
            SetComboValue(CboTierRisk, kpi?.TierRisk);
            TxtComplianceComments.Text = kpi?.Comments ?? "";
        }

        private void BtnSaveCompliance_Click(object sender, RoutedEventArgs e)
        {
            var partner = LstPartners.SelectedItem as Partner;
            var country = CboCountry.SelectedItem as Country;
            if (partner == null || country == null) return;
            int period = GetSelectedPeriod();

            string certsNeeded = (CboCertsNeeded.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "No";
            int? req = ParseInt(TxtRequiredCerts.Text);
            double? covered = ParseDouble(TxtCertsCovered.Text);
            int? exp3 = ParseInt(TxtCertsExp3.Text);
            int? exp6 = ParseInt(TxtCertsExp6.Text);
            int? exp12 = ParseInt(TxtCertsExp12.Text);
            string status = (CboComplianceStatus.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "OK";
            string risk = (CboTierRisk.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "No";

            try
            {
                using var connection = DatabaseHelper.GetConnection();
                connection.Open();
                connection.Execute(@"
                    INSERT INTO KPI_Compliance (PartnerId, CountryId, Period, CertificationsNeeded, RequiredCertifications, CertificationsCovered, CertsExpiring3Months, CertsExpiring6Months, CertsExpiring12Months, ProgramComplianceStatus, TierRisk, Comments)
                    VALUES (@PartnerId, @CountryId, @Period, @CertificationsNeeded, @RequiredCertifications, @CertificationsCovered, @CertsExpiring3Months, @CertsExpiring6Months, @CertsExpiring12Months, @ProgramComplianceStatus, @TierRisk, @Comments)
                    ON CONFLICT(PartnerId, CountryId, Period) DO UPDATE SET
                        CertificationsNeeded=@CertificationsNeeded, RequiredCertifications=@RequiredCertifications, CertificationsCovered=@CertificationsCovered,
                        CertsExpiring3Months=@CertsExpiring3Months, CertsExpiring6Months=@CertsExpiring6Months, CertsExpiring12Months=@CertsExpiring12Months,
                        ProgramComplianceStatus=@ProgramComplianceStatus, TierRisk=@TierRisk, Comments=@Comments;",
                    new
                    {
                        PartnerId = partner.Id,
                        CountryId = country.Id,
                        Period = period,
                        CertificationsNeeded = certsNeeded,
                        RequiredCertifications = req,
                        CertificationsCovered = covered,
                        CertsExpiring3Months = exp3,
                        CertsExpiring6Months = exp6,
                        CertsExpiring12Months = exp12,
                        ProgramComplianceStatus = status,
                        TierRisk = risk,
                        Comments = TxtComplianceComments.Text
                    });

                MessageBox.Show("Compliance KPIs saved!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                
                var mainWin = Window.GetWindow(this) as MainWindow;
                mainWin?.RefreshAll();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving Compliance KPIs: {ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadProgramControlKpis(System.Data.IDbConnection conn, int partnerId, int countryId, int period)
        {
            var kpi = conn.QueryFirstOrDefault<KPI_ProgramControl>(
                "SELECT * FROM KPI_ProgramControl WHERE PartnerId = @PartnerId AND CountryId = @CountryId AND Period = @Period",
                new { PartnerId = partnerId, CountryId = countryId, Period = period });

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
            var country = CboCountry.SelectedItem as Country;
            if (partner == null || country == null) return;
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
                    INSERT INTO KPI_ProgramControl (PartnerId, CountryId, Period, ReportedRevenueOnitio, ReportedRevenueOem, Variance, RebateEligibility, TierProgress, RiskOfDowngrade, Comments)
                    VALUES (@PartnerId, @CountryId, @Period, @ReportedRevenueOnitio, @ReportedRevenueOem, @Variance, @RebateEligibility, @TierProgress, @RiskOfDowngrade, @Comments)
                    ON CONFLICT(PartnerId, CountryId, Period) DO UPDATE SET
                        ReportedRevenueOnitio=@ReportedRevenueOnitio, ReportedRevenueOem=@ReportedRevenueOem, Variance=@Variance,
                        RebateEligibility=@RebateEligibility, TierProgress=@TierProgress, RiskOfDowngrade=@RiskOfDowngrade, Comments=@Comments;",
                    new
                    {
                        PartnerId = partner.Id,
                        CountryId = country.Id,
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

        private void LoadEsgKpis(System.Data.IDbConnection conn, int partnerId, int countryId, int period)
        {
            var kpi = conn.QueryFirstOrDefault<KPI_SustainabilityESG>(
                "SELECT * FROM KPI_SustainabilityESG WHERE PartnerId = @PartnerId AND CountryId = @CountryId AND Period = @Period",
                new { PartnerId = partnerId, CountryId = countryId, Period = period });

            SetComboValue(CboEsgTakeBack, kpi?.TakeBackRecyclingProgram);
            SetComboValue(CboEsgRefurb, kpi?.RefurbSecondLifeSupport);
            SetComboValue(CboEsgEnvData, kpi?.EnvironmentalDataAvailable);
            SetComboValue(CboEsgCompliance, kpi?.EsgComplianceStatus);
            TxtEsgLogistics.Text = kpi?.LogisticsEmissionReduction ?? "";
            SetComboValue(CboEsgQualMet, kpi?.SustainabilityQualificationMet);
            TxtEsgComments.Text = kpi?.Comments ?? "";
        }

        private void BtnSaveEsg_Click(object sender, RoutedEventArgs e)
        {
            var partner = LstPartners.SelectedItem as Partner;
            var country = CboCountry.SelectedItem as Country;
            if (partner == null || country == null) return;
            int period = GetSelectedPeriod();

            string takeback = (CboEsgTakeBack.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "No";
            string refurb = (CboEsgRefurb.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "No";
            string env = (CboEsgEnvData.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "No";
            string compliance = (CboEsgCompliance.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "OK";
            string qual = (CboEsgQualMet.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "No";

            try
            {
                using var connection = DatabaseHelper.GetConnection();
                connection.Open();
                connection.Execute(@"
                    INSERT INTO KPI_SustainabilityESG (PartnerId, CountryId, Period, TakeBackRecyclingProgram, RefurbSecondLifeSupport, EnvironmentalDataAvailable, EsgComplianceStatus, LogisticsEmissionReduction, SustainabilityQualificationMet, Comments)
                    VALUES (@PartnerId, @CountryId, @Period, @TakeBackRecyclingProgram, @RefurbSecondLifeSupport, @EnvironmentalDataAvailable, @EsgComplianceStatus, @LogisticsEmissionReduction, @SustainabilityQualificationMet, @Comments)
                    ON CONFLICT(PartnerId, CountryId, Period) DO UPDATE SET
                        TakeBackRecyclingProgram=@TakeBackRecyclingProgram, RefurbSecondLifeSupport=@RefurbSecondLifeSupport, EnvironmentalDataAvailable=@EnvironmentalDataAvailable,
                        EsgComplianceStatus=@EsgComplianceStatus, LogisticsEmissionReduction=@LogisticsEmissionReduction, SustainabilityQualificationMet=@SustainabilityQualificationMet, Comments=@Comments;",
                    new
                    {
                        PartnerId = partner.Id,
                        CountryId = country.Id,
                        Period = period,
                        TakeBackRecyclingProgram = takeback,
                        RefurbSecondLifeSupport = refurb,
                        EnvironmentalDataAvailable = env,
                        EsgComplianceStatus = compliance,
                        LogisticsEmissionReduction = TxtEsgLogistics.Text,
                        SustainabilityQualificationMet = qual,
                        Comments = TxtEsgComments.Text
                    });

                MessageBox.Show("Sustainability ESG KPIs saved!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving Sustainability ESG KPIs: {ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadOperationalKpis(System.Data.IDbConnection conn, int partnerId, int period)
        {
            var kpi = conn.QueryFirstOrDefault<KPI_Operational>(
                "SELECT * FROM KPI_Operational WHERE PartnerId = @PartnerId AND Period = @Period",
                new { PartnerId = partnerId, Period = period });

            TxtOpDoaRate.Text = kpi?.DoaRate?.ToString() ?? "";
            TxtOpRmaTime.Text = kpi?.AvgRmaLeadTime?.ToString() ?? "";
            TxtOpDataQuality.Text = kpi?.AssetDataQuality?.ToString() ?? "";
            TxtOpIssuesCount.Text = kpi?.OperationalIssuesCount?.ToString() ?? "";
            SetComboValue(CboOpTargetMet, kpi?.TargetMet);
            TxtOpComments.Text = kpi?.Comments ?? "";
        }

        private void BtnSaveOperational_Click(object sender, RoutedEventArgs e)
        {
            var partner = LstPartners.SelectedItem as Partner;
            if (partner == null) return;
            int period = GetSelectedPeriod();

            double? doa = ParseDouble(TxtOpDoaRate.Text);
            double? rma = ParseDouble(TxtOpRmaTime.Text);
            double? dataQuality = ParseDouble(TxtOpDataQuality.Text);
            int? issues = ParseInt(TxtOpIssuesCount.Text);
            string targetMet = (CboOpTargetMet.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "No";

            try
            {
                using var connection = DatabaseHelper.GetConnection();
                connection.Open();
                connection.Execute(@"
                    INSERT INTO KPI_Operational (PartnerId, Period, DoaRate, AvgRmaLeadTime, AssetDataQuality, OperationalIssuesCount, TargetMet, Comments)
                    VALUES (@PartnerId, @Period, @DoaRate, @AvgRmaLeadTime, @AssetDataQuality, @OperationalIssuesCount, @TargetMet, @Comments)
                    ON CONFLICT(PartnerId, Period) DO UPDATE SET
                        DoaRate=@DoaRate, AvgRmaLeadTime=@AvgRmaLeadTime, AssetDataQuality=@AssetDataQuality,
                        OperationalIssuesCount=@OperationalIssuesCount, TargetMet=@TargetMet, Comments=@Comments;",
                    new
                    {
                        PartnerId = partner.Id,
                        Period = period,
                        DoaRate = doa,
                        AvgRmaLeadTime = rma,
                        AssetDataQuality = dataQuality,
                        OperationalIssuesCount = issues,
                        TargetMet = targetMet,
                        Comments = TxtOpComments.Text
                    });

                MessageBox.Show("Operational KPIs saved!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving Operational KPIs: {ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadStrategicKpis(System.Data.IDbConnection conn, int partnerId, int period)
        {
            var kpi = conn.QueryFirstOrDefault<KPI_Strategic>(
                "SELECT * FROM KPI_Strategic WHERE PartnerId = @PartnerId AND Period = @Period",
                new { PartnerId = partnerId, Period = period });

            SetComboValue(CboStratStd, kpi?.DegreeOfStandardization);
            TxtStratDeployTime.Text = kpi?.AvgTimeToDeploy?.ToString() ?? "";
            TxtStratImpactScore.Text = kpi?.CustomerImpactScore?.ToString() ?? "";
            TxtStratFit.Text = kpi?.StrategicFitAssessment ?? "";
            TxtStratComments.Text = kpi?.Comments ?? "";
        }

        private void BtnSaveStrategic_Click(object sender, RoutedEventArgs e)
        {
            var partner = LstPartners.SelectedItem as Partner;
            if (partner == null) return;
            int period = GetSelectedPeriod();

            string std = (CboStratStd.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Medium";
            double? deploy = ParseDouble(TxtStratDeployTime.Text);
            double? impact = ParseDouble(TxtStratImpactScore.Text);

            try
            {
                using var connection = DatabaseHelper.GetConnection();
                connection.Open();
                connection.Execute(@"
                    INSERT INTO KPI_Strategic (PartnerId, Period, DegreeOfStandardization, AvgTimeToDeploy, CustomerImpactScore, StrategicFitAssessment, Comments)
                    VALUES (@PartnerId, @Period, @DegreeOfStandardization, @AvgTimeToDeploy, @CustomerImpactScore, @StrategicFitAssessment, @Comments)
                    ON CONFLICT(PartnerId, Period) DO UPDATE SET
                        DegreeOfStandardization=@DegreeOfStandardization, AvgTimeToDeploy=@AvgTimeToDeploy,
                        CustomerImpactScore=@CustomerImpactScore, StrategicFitAssessment=@StrategicFitAssessment, Comments=@Comments;",
                    new
                    {
                        PartnerId = partner.Id,
                        Period = period,
                        DegreeOfStandardization = std,
                        AvgTimeToDeploy = deploy,
                        CustomerImpactScore = impact,
                        StrategicFitAssessment = TxtStratFit.Text,
                        Comments = TxtStratComments.Text
                    });

                MessageBox.Show("Strategic KPIs saved!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving Strategic KPIs: {ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

        private int? ParseInt(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (int.TryParse(text, out int val)) return val;
            return null;
        }
    }
}