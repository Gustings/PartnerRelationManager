using System;
using System.Windows;
using PartnerRelationManager.Services;

namespace PartnerRelationManager
{
    public partial class MainWindow : Window
    {
        private DashboardView dashboardView;
        private PartnersView partnersView;
        private SettingsView settingsView;

        public MainWindow()
        {
            InitializeComponent();
            
            // Check if database needs onboarding setup
            if (DatabaseHelper.IsDatabaseEmpty())
            {
                var onboarding = new OnboardingWindow();
                bool? result = onboarding.ShowDialog();
                if (result != true)
                {
                    Application.Current.Shutdown();
                    return;
                }
            }

            // Initialize views
            dashboardView = new DashboardView();
            partnersView = new PartnersView();
            settingsView = new SettingsView();

            // Set DB path string on footer
            TxtDbPath.Text = DatabaseHelper.DatabasePath;

            // Load default view
            MainContent.Content = dashboardView;
            
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshAll();
        }

        public void RefreshAll()
        {
            dashboardView?.RefreshDashboard();
            partnersView?.RefreshAll();
        }

        private void BtnNavDashboard_Click(object sender, RoutedEventArgs e)
        {
            MainContent.Content = dashboardView;
            dashboardView.RefreshDashboard();
            
            BtnNavDashboard.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
            BtnNavPartners.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
            BtnNavSettings.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
        }

        private void BtnNavPartners_Click(object sender, RoutedEventArgs e)
        {
            MainContent.Content = partnersView;
            partnersView.RefreshAll();
            
            BtnNavDashboard.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
            BtnNavPartners.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
            BtnNavSettings.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
        }

        private void BtnNavSettings_Click(object sender, RoutedEventArgs e)
        {
            MainContent.Content = settingsView;
            
            BtnNavDashboard.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
            BtnNavPartners.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;
            BtnNavSettings.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
        }
    }
}