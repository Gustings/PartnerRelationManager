using System;
using System.Threading.Tasks;
using System.Windows;
using PartnerRelationManager.Services;

namespace PartnerRelationManager
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DatabaseHelper.InitializeDatabase();

            // Run update check in a background task
            Task.Run(async () =>
            {
                await Task.Delay(3000); // Wait 3 seconds for main UI to load first
                await CheckForUpdatesOnStartupAsync();
            });
        }

        private async Task CheckForUpdatesOnStartupAsync()
        {
            try
            {
                var updateInfo = await UpdateService.CheckForUpdatesAsync();
                if (updateInfo.IsUpdateAvailable && !Dispatcher.HasShutdownStarted)
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (Dispatcher.HasShutdownStarted) return;

                        var result = MessageBox.Show(
                            $"A new version ({updateInfo.LatestVersion}) of Partner Relation Manager is available.\n\n" +
                            $"Release Notes:\n{updateInfo.ReleaseNotes}\n\n" +
                            "Would you like to download and install this update now?",
                            "Update Available",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question
                        );

                        if (result == MessageBoxResult.Yes)
                        {
                            // Run the download and install in a background thread
                            Task.Run(async () =>
                            {
                                await UpdateService.DownloadAndInstallUpdateAsync(updateInfo.DownloadUrl);
                            });
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Update check failed: {ex.Message}");
            }
        }
    }
}
