using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Windows;

namespace PartnerRelationManager
{
    public class UpdateInfo
    {
        public bool IsUpdateAvailable { get; set; }
        public string LatestVersion { get; set; } = string.Empty;
        public string ReleaseNotes { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
    }

    public static class UpdateService
    {
        private static readonly HttpClient httpClient = new HttpClient();
        private const string RepoUrl = "https://api.github.com/repos/Gustings/PartnerRelationManager/releases/latest";

        static UpdateService()
        {
            // GitHub API requires a User-Agent header
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("PartnerRelationManager-Updater");
        }

        public static async Task<UpdateInfo> CheckForUpdatesAsync()
        {
            var updateInfo = new UpdateInfo();

            try
            {
                var response = await httpClient.GetStringAsync(RepoUrl);
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                if (root.TryGetProperty("tag_name", out var tagProperty))
                {
                    string tag = tagProperty.GetString() ?? string.Empty;
                    string cleanTag = tag.TrimStart('v', 'V');

                    if (Version.TryParse(cleanTag, out Version? latestVersion))
                    {
                        Version currentVersion = typeof(UpdateService).Assembly.GetName().Version ?? new Version(1, 0, 0, 0);

                        if (latestVersion > currentVersion)
                        {
                            updateInfo.IsUpdateAvailable = true;
                            updateInfo.LatestVersion = tag;

                            if (root.TryGetProperty("body", out var bodyProp))
                            {
                                updateInfo.ReleaseNotes = bodyProp.GetString() ?? string.Empty;
                            }

                            if (root.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var asset in assetsProp.EnumerateArray())
                                {
                                    if (asset.TryGetProperty("name", out var nameProp) && asset.TryGetProperty("browser_download_url", out var urlProp))
                                    {
                                        string name = nameProp.GetString() ?? string.Empty;
                                        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                        {
                                            updateInfo.DownloadUrl = urlProp.GetString() ?? string.Empty;
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking for updates: {ex.Message}");
            }

            return updateInfo;
        }

        public static async Task DownloadAndInstallUpdateAsync(string downloadUrl)
        {
            if (string.IsNullOrEmpty(downloadUrl)) return;

            try
            {
                string tempDir = Path.GetTempPath();
                // Use a unique name to avoid write permission locks / conflicts
                string tempPath = Path.Combine(tempDir, $"PartnerRelationManagerSetup_{Guid.NewGuid():N}.exe");

                // Download the installer
                using (var response = await httpClient.GetAsync(downloadUrl))
                {
                    response.EnsureSuccessStatusCode();
                    using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await response.Content.CopyToAsync(fileStream);
                    }
                }

                // Verify file exists
                if (File.Exists(tempPath))
                {
                    // Run the installer silently in the background
                    var psi = new ProcessStartInfo
                    {
                        FileName = tempPath,
                        Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART",
                        UseShellExecute = true
                    };
                    Process.Start(psi);

                    // Exit the current application to let the installer overwrite files
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        Application.Current?.Shutdown();
                    });
                }
            }
            catch (Exception ex)
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    MessageBox.Show($"Error running the updater: {ex.Message}", "Update Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }
    }
}
