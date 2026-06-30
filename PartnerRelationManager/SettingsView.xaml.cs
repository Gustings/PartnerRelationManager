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
        public SettingsView()
        {
            InitializeComponent();
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
                    
                    // Trigger a refresh of the application dashboard and partner lists
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

        private void BtnAddCountry_Click(object sender, RoutedEventArgs e)
        {
            string name = TxtCountryName.Text.Trim();
            string code = TxtCountryCode.Text.Trim().ToUpper();

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(code))
            {
                MessageBox.Show("Please fill out both Name and Code.", "Validation Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                using var connection = DatabaseHelper.GetConnection();
                connection.Open();

                var existing = connection.QueryFirstOrDefault(
                    "SELECT * FROM Countries WHERE Code = @Code OR Name = @Name",
                    new { Code = code, Name = name }
                );

                if (existing != null)
                {
                    MessageBox.Show($"A country with the name '{name}' or code '{code}' already exists.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                connection.Execute(
                    "INSERT INTO Countries (Name, Code) VALUES (@Name, @Code);",
                    new { Name = name, Code = code }
                );

                MessageBox.Show($"Country '{name}' ({code}) added successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                TxtCountryName.Clear();
                TxtCountryCode.Clear();

                var mainWindow = Window.GetWindow(this) as MainWindow;
                mainWindow?.RefreshAll();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error adding country: {ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnAddTier_Click(object sender, RoutedEventArgs e)
        {
            string name = TxtTierName.Text.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Please specify a Tier Name.", "Validation Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                using var connection = DatabaseHelper.GetConnection();
                connection.Open();

                var existing = connection.QueryFirstOrDefault(
                    "SELECT * FROM Tiers WHERE Name = @Name",
                    new { Name = name }
                );

                if (existing != null)
                {
                    MessageBox.Show($"A program tier with the name '{name}' already exists.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                connection.Execute(
                    "INSERT INTO Tiers (Name) VALUES (@Name);",
                    new { Name = name }
                );

                MessageBox.Show($"Tier '{name}' added successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                TxtTierName.Clear();

                var mainWindow = Window.GetWindow(this) as MainWindow;
                mainWindow?.RefreshAll();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error adding tier: {ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
