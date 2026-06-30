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

        private Partner? GetSelectedPartner()
        {
            var selectedItem = TreePartners.SelectedItem as PartnerTreeItem;
            return selectedItem?.PartnerRef;
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

        private string GetOemBaseName(Partner p)
        {
            string name = p.Name;
            string[] suffixes = { " Norway", " Sweden", " Denmark", " Finland", " NO", " SE", " DK", " FI" };
            foreach (var suffix in suffixes)
            {
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return name.Substring(0, name.Length - suffix.Length).Trim();
                }
            }
            return name;
        }

        private string GetStatusColorHex(string status)
        {
            if (string.IsNullOrWhiteSpace(status)) return "#FFAAAAAA"; // Gray
            switch (status.Trim().ToUpperInvariant())
            {
                case "GREEN": return "#FF26BC74";
                case "AMBER":
                case "ORANGE": return "#FFF59B23";
                case "RED": return "#FFE81123";
                default: return "#FFAAAAAA";
            }
        }

        private void FilterPartnersList()
        {
            string filterText = TxtSearchPartner.Text.Trim();
            List<Partner> filtered = allPartners;
            if (!string.IsNullOrEmpty(filterText))
            {
                filtered = allPartners
                    .Where(p => p.Name.Contains(filterText, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            // Build hierarchical tree
            var treeItems = new List<PartnerTreeItem>();
            var groups = filtered.GroupBy(p => GetOemBaseName(p));
            foreach (var g in groups)
            {
                var folder = new PartnerTreeItem
                {
                    DisplayText = g.Key,
                    PartnerRef = null,
                    Children = g.Select(p => new PartnerTreeItem
                    {
                        DisplayText = string.IsNullOrEmpty(p.CountryCode) ? p.Name : p.CountryCode.ToUpperInvariant(),
                        PartnerRef = p,
                        StatusColor = GetStatusColorHex(p.Status)
                    }).ToList()
                };
                treeItems.Add(folder);
            }

            TreePartners.ItemsSource = treeItems;
        }

        private void TxtSearchPartner_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterPartnersList();
        }

        private void TreePartners_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var selectedItem = TreePartners.SelectedItem as PartnerTreeItem;
            if (selectedItem == null || selectedItem.PartnerRef == null)
            {
                CardNoSelection.Visibility = Visibility.Visible;
                GridPartnerDetails.Visibility = Visibility.Collapsed;
                return;
            }

            var partner = selectedItem.PartnerRef;
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
            var partner = GetSelectedPartner();
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

                // 4. Compliance KPIs
                var comp = connection.QueryFirstOrDefault<KPI_Compliance>(
                    "SELECT * FROM KPI_Compliance WHERE PartnerId = @PartnerId AND Period = @Period",
                    new { PartnerId = partner.Id, Period = period });

                if (comp != null)
                {
                    string certsNeeded = comp.CertificationsNeeded ?? "No";
                    CboCompCertsNeeded.SelectedIndex = certsNeeded.Trim().Equals("Yes", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                    TxtCompRequiredCerts.Text = comp.RequiredCertifications?.ToString() ?? "0";
                    TxtCompCertsCovered.Text = (comp.CertificationsCovered * 100.0)?.ToString("F1") ?? "0.0";
                    TxtCompExp3m.Text = comp.CertsExpiring3Months?.ToString() ?? "0";
                    TxtCompExp6m.Text = comp.CertsExpiring6Months?.ToString() ?? "0";
                    TxtCompExp12m.Text = comp.CertsExpiring12Months?.ToString() ?? "0";
                    TxtCompStatus.Text = comp.ProgramComplianceStatus;
                    TxtCompTierRisk.Text = comp.TierRisk;
                    TxtCompComments.Text = comp.Comments;
                }
                else
                {
                    CboCompCertsNeeded.SelectedIndex = 1; // "No"
                    TxtCompRequiredCerts.Text = "0";
                    TxtCompCertsCovered.Text = "0.0";
                    TxtCompExp3m.Text = "0";
                    TxtCompExp6m.Text = "0";
                    TxtCompExp12m.Text = "0";
                    TxtCompStatus.Text = "OK";
                    TxtCompTierRisk.Text = "No";
                    TxtCompComments.Text = string.Empty;
                }

                UpdateCertificationFieldsEnabledState();
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
            var partner = GetSelectedPartner();
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
                
                // Reselect partner from the reloaded list in tree
                var targetItem = FindTreeItem(TreePartners.ItemsSource as IEnumerable<PartnerTreeItem>, selectedId);
                if (targetItem != null)
                {
                    targetItem.IsSelected = true;
                    ExpandParent(TreePartners.ItemsSource as IEnumerable<PartnerTreeItem>, targetItem);
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

        private void BtnSaveCompliance_Click(object sender, RoutedEventArgs e)
        {
            var partner = GetSelectedPartner();
            if (partner == null) return;
            int period = GetSelectedPeriod();

            try
            {
                using var connection = DatabaseHelper.GetConnection();
                connection.Open();

                string certsNeeded = "No";
                if (CboCompCertsNeeded.SelectedItem is ComboBoxItem item)
                {
                    certsNeeded = item.Content?.ToString() ?? "No";
                }
                int reqCerts = int.TryParse(TxtCompRequiredCerts.Text, out int req) ? req : 0;
                
                double certsCovered = 0.0;
                if (double.TryParse(TxtCompCertsCovered.Text.Replace("%", "").Trim(), out double cov))
                {
                    certsCovered = cov / 100.0;
                }
                
                int exp3m = int.TryParse(TxtCompExp3m.Text, out int e3) ? e3 : 0;
                int exp6m = int.TryParse(TxtCompExp6m.Text, out int e6) ? e6 : 0;
                int exp12m = int.TryParse(TxtCompExp12m.Text, out int e12) ? e12 : 0;
                string status = TxtCompStatus.Text.Trim();
                string tierRisk = TxtCompTierRisk.Text.Trim();
                string comments = TxtCompComments.Text.Trim();

                connection.Execute(@"
                    INSERT INTO KPI_Compliance (PartnerId, Period, CertificationsNeeded, RequiredCertifications, CertificationsCovered, CertsExpiring3Months, CertsExpiring6Months, CertsExpiring12Months, ProgramComplianceStatus, TierRisk, Comments)
                    VALUES (@PartnerId, @Period, @CertificationsNeeded, @RequiredCertifications, @CertificationsCovered, @CertsExpiring3Months, @CertsExpiring6Months, @CertsExpiring12Months, @ProgramComplianceStatus, @TierRisk, @Comments)
                    ON CONFLICT(PartnerId, Period) DO UPDATE SET
                        CertificationsNeeded = @CertificationsNeeded,
                        RequiredCertifications = @RequiredCertifications,
                        CertificationsCovered = @CertificationsCovered,
                        CertsExpiring3Months = @CertsExpiring3Months,
                        CertsExpiring6Months = @CertsExpiring6Months,
                        CertsExpiring12Months = @CertsExpiring12Months,
                        ProgramComplianceStatus = @ProgramComplianceStatus,
                        TierRisk = @TierRisk,
                        Comments = @Comments;",
                    new
                    {
                        PartnerId = partner.Id,
                        Period = period,
                        CertificationsNeeded = certsNeeded,
                        RequiredCertifications = reqCerts,
                        CertificationsCovered = certsCovered,
                        CertsExpiring3Months = exp3m,
                        CertsExpiring6Months = exp6m,
                        CertsExpiring12Months = exp12m,
                        ProgramComplianceStatus = status,
                        TierRisk = tierRisk,
                        Comments = comments
                    });

                MessageBox.Show("Compliance KPIs saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);

                // Refresh dashboard/main UI
                var mainWin = Window.GetWindow(this) as MainWindow;
                mainWin?.RefreshAll();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving compliance KPIs: {ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CboCompCertsNeeded_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateCertificationFieldsEnabledState();
        }

        private void UpdateCertificationFieldsEnabledState()
        {
            if (CboCompCertsNeeded == null) return;
            
            var selectedItem = CboCompCertsNeeded.SelectedItem as ComboBoxItem;
            string value = selectedItem?.Content?.ToString() ?? "No";
            bool certsNeeded = string.Equals(value, "Yes", StringComparison.OrdinalIgnoreCase);

            if (TxtCompRequiredCerts != null) TxtCompRequiredCerts.IsEnabled = certsNeeded;
            if (TxtCompCertsCovered != null) TxtCompCertsCovered.IsEnabled = certsNeeded;
            if (TxtCompStatus != null) TxtCompStatus.IsEnabled = certsNeeded;
            if (TxtCompExp3m != null) TxtCompExp3m.IsEnabled = certsNeeded;
            if (TxtCompExp6m != null) TxtCompExp6m.IsEnabled = certsNeeded;
            if (TxtCompExp12m != null) TxtCompExp12m.IsEnabled = certsNeeded;
            if (TxtCompTierRisk != null) TxtCompTierRisk.IsEnabled = certsNeeded;
            if (TxtCompComments != null) TxtCompComments.IsEnabled = certsNeeded;
        }

        // ==========================================
        // SUB-ITEMS ACTIONS (Contacts, Activities, Documents)
        // ==========================================
        private void BtnAddContact_Click(object sender, RoutedEventArgs e)
        {
            var partner = GetSelectedPartner();
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
            var partner = GetSelectedPartner();
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
            var addWindow = new AddPartnerWindow();
            addWindow.Owner = Window.GetWindow(this);
            if (addWindow.ShowDialog() == true)
            {
                LoadPartners();
                
                // Refresh dashboard/main UI
                var mainWin = Window.GetWindow(this) as MainWindow;
                mainWin?.RefreshAll();
            }
        }

        private void MenuItemDeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = TreePartners.SelectedItem as PartnerTreeItem;
            if (selectedItem == null)
            {
                MessageBox.Show("Please select a partner or partner group to delete.", "Delete", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool isParent = selectedItem.PartnerRef == null;
            string entityName = selectedItem.DisplayText;

            MessageBoxResult confirmResult;
            List<int> partnerIdsToDelete = new List<int>();

            if (isParent)
            {
                partnerIdsToDelete = selectedItem.Children
                    .Where(c => c.PartnerRef != null)
                    .Select(c => c.PartnerRef!.Id)
                    .ToList();

                if (partnerIdsToDelete.Count == 0)
                {
                    confirmResult = MessageBox.Show($"Are you sure you want to delete the empty partner group '{entityName}'?", "Confirm Deletion", MessageBoxButton.YesNo, MessageBoxImage.Question);
                }
                else
                {
                    confirmResult = MessageBox.Show($"Are you sure you want to delete the partner group '{entityName}' and all its {partnerIdsToDelete.Count} country accounts, including all associated contacts, activities, documents, and KPIs?", "Confirm Deletion", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                }
            }
            else
            {
                var partner = selectedItem.PartnerRef!;
                partnerIdsToDelete.Add(partner.Id);
                confirmResult = MessageBox.Show($"Are you sure you want to delete the partner '{partner.Name}' and all associated contacts, activities, documents, and KPIs?", "Confirm Deletion", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            }

            if (confirmResult != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                using var connection = DatabaseHelper.GetConnection();
                connection.Open();
                using var transaction = connection.BeginTransaction();
                try
                {
                    if (partnerIdsToDelete.Count > 0)
                    {
                        // Delete from KPI_ProgramControl
                        connection.Execute("DELETE FROM KPI_ProgramControl WHERE PartnerId IN @Ids", new { Ids = partnerIdsToDelete }, transaction);

                        // Delete from KPI_Commercial
                        connection.Execute("DELETE FROM KPI_Commercial WHERE PartnerId IN @Ids", new { Ids = partnerIdsToDelete }, transaction);

                        // Delete from KPI_Compliance
                        connection.Execute("DELETE FROM KPI_Compliance WHERE PartnerId IN @Ids", new { Ids = partnerIdsToDelete }, transaction);

                        // Delete from Documents
                        connection.Execute("DELETE FROM Documents WHERE PartnerId IN @Ids", new { Ids = partnerIdsToDelete }, transaction);

                        // Delete from Activities
                        connection.Execute("DELETE FROM Activities WHERE PartnerId IN @Ids", new { Ids = partnerIdsToDelete }, transaction);

                        // Delete from Contacts
                        connection.Execute("DELETE FROM Contacts WHERE PartnerId IN @Ids", new { Ids = partnerIdsToDelete }, transaction);

                        // Delete from Partners
                        connection.Execute("DELETE FROM Partners WHERE Id IN @Ids", new { Ids = partnerIdsToDelete }, transaction);
                    }

                    transaction.Commit();

                    MessageBox.Show("Selected partner data deleted successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);

                    // Clear selections/views
                    CardNoSelection.Visibility = Visibility.Visible;
                    GridPartnerDetails.Visibility = Visibility.Collapsed;

                    LoadPartners();

                    // Refresh dashboard/main UI
                    var mainWin = Window.GetWindow(this) as MainWindow;
                    mainWin?.RefreshAll();
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    MessageBox.Show($"Error during deletion: {ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Database connection error: {ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ==========================================
        // DOCUMENT MANAGEMENT
        // ==========================================
        private void BtnUploadDoc_Click(object sender, RoutedEventArgs e)
        {
            var partner = GetSelectedPartner();
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
        // HELPERS
        // ==========================================
        private PartnerTreeItem? FindTreeItem(IEnumerable<PartnerTreeItem>? items, int partnerId)
        {
            if (items == null) return null;
            foreach (var item in items)
            {
                if (item.PartnerRef?.Id == partnerId) return item;
                var child = FindTreeItem(item.Children, partnerId);
                if (child != null) return child;
            }
            return null;
        }

        private bool ExpandParent(IEnumerable<PartnerTreeItem>? items, PartnerTreeItem target)
        {
            if (items == null) return false;
            foreach (var item in items)
            {
                if (item.Children.Contains(target))
                {
                    item.IsExpanded = true;
                    return true;
                }
                if (ExpandParent(item.Children, target))
                {
                    item.IsExpanded = true;
                    return true;
                }
            }
            return false;
        }
    }

    public class PartnerTreeItem : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _isSelected;
        private bool _isExpanded;

        public string DisplayText { get; set; } = string.Empty;
        public string StatusColor { get; set; } = "#FFAAAAAA";
        public Partner? PartnerRef { get; set; }
        public List<PartnerTreeItem> Children { get; set; } = new();

        public Visibility FolderIconVisibility => PartnerRef == null ? Visibility.Visible : Visibility.Collapsed;
        public Visibility StatusDotVisibility => PartnerRef != null ? Visibility.Visible : Visibility.Collapsed;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged(nameof(IsExpanded));
                }
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
        }
    }
}