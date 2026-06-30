using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Dapper;
using PartnerRelationManager.Services;

namespace PartnerRelationManager
{
    public partial class SettingsView : UserControl
    {
        private bool _isInitializing = true;

        public SettingsView()
        {
            InitializeComponent();
            LoadLocalCountry();
        }

        private void LoadLocalCountry()
        {
            _isInitializing = true;
            string localCountry = DatabaseHelper.GetSetting("LocalCountry", "NO");
            foreach (ComboBoxItem item in CboLocalCountry.Items)
            {
                if (item.Content.ToString() == localCountry)
                {
                    CboLocalCountry.SelectedItem = item;
                    break;
                }
            }
            _isInitializing = false;
        }

        private void CboLocalCountry_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            if (CboLocalCountry.SelectedItem is ComboBoxItem item)
            {
                string selectedCountry = item.Content?.ToString() ?? "NO";
                DatabaseHelper.SaveSetting("LocalCountry", selectedCountry);
                
                var mainWindow = Window.GetWindow(this) as MainWindow;
                mainWindow?.RefreshAll();
            }
        }

        private void BtnImportExcel_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Excel Files (*.xlsx)|*.xlsx|All Files (*.*)|*.*",
                Title = "Select Onitio One Partner KPI Tracker Excel File"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    ExcelService.ImportTrackerData(openFileDialog.FileName);
                    MessageBox.Show("Excel tracker data imported successfully!", "Import Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    var mainWindow = Window.GetWindow(this) as MainWindow;
                    mainWindow?.RefreshAll();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error importing Excel data: {ex.Message}", "Import Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            var saveFileDialog = new SaveFileDialog
            {
                Filter = "Excel Files (*.xlsx)|*.xlsx",
                DefaultExt = ".xlsx",
                FileName = $"SRM_Export_{DateTime.Now:yyyyMMdd}",
                Title = "Export SRM Data to Excel"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    ExcelService.ExportData(saveFileDialog.FileName);
                    MessageBox.Show("SRM data exported successfully!", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error exporting SRM data: {ex.Message}", "Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnResetDatabase_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Are you absolutely sure you want to RESET the database? All records will be permanently deleted.",
                "Confirm Database Reset",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    DatabaseHelper.ResetDatabase();
                    MessageBox.Show("Database reset successfully! Running onboarding setup again...", "Success", MessageBoxButton.OK, MessageBoxImage.Information);

                    var onboarding = new OnboardingWindow();
                    var onboardingResult = onboarding.ShowDialog();

                    var mainWindow = Window.GetWindow(this) as MainWindow;
                    mainWindow?.RefreshAll();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error resetting database: {ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
