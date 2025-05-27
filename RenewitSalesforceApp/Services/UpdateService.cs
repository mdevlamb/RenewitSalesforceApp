using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace RenewitSalesforceApp.Services
{
    public class UpdateService
    {
        private const string VERSION_URL = "https://mdevlamb.github.io/RenewitSalesforceApp/version.json";
        private readonly HttpClient _client;

        public UpdateService()
        {
            _client = new HttpClient();
            _client.DefaultRequestHeaders.Add("User-Agent", "Renew-it Salesforce App");
            _client.DefaultRequestHeaders.Add("Cache-Control", "no-cache");
        }

        public async Task CheckForUpdates()
        {
            try
            {
                // Get current app version
                var currentVersion = new Version(AppInfo.VersionString);
                int currentBuild = int.Parse(AppInfo.BuildString);

                // Add timestamp to bypass caching
                string url = $"{VERSION_URL}?t={DateTime.Now.Ticks}";
                string json = await _client.GetStringAsync(url);

                var latestInfo = JsonSerializer.Deserialize<AppVersionInfo>(
                    json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );

                if (latestInfo == null) return;

                // Parse version
                var latestVersion = new Version(latestInfo.Version);

                // Check if update available
                bool updateAvailable = latestVersion > currentVersion ||
                                      (latestVersion == currentVersion &&
                                       latestInfo.BuildNumber > currentBuild);

                if (updateAvailable)
                {
                    // Use MainThread for UI operations
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        // Customize update dialog for Renew-it
                        string title = latestInfo.Required ?
                            "Required Update" : "Update Available";

                        string message = $"Version {latestInfo.Version} (Build {latestInfo.BuildNumber}) is available.\n\n" +
                                       $"What's New:\n{latestInfo.Notes}\n\n" +
                                       $"Released: {latestInfo.ReleaseDate:MMM d, yyyy}";

                        string acceptButton = "Download Now";
                        string cancelButton = latestInfo.Required ? null : "Later";

                        // Show alert
                        bool download = await Application.Current.MainPage.DisplayAlert(
                            title, message, acceptButton, cancelButton);

                        if (download || latestInfo.Required)
                        {
                            // Open browser to download page
                            await Browser.OpenAsync(latestInfo.DownloadUrl);

                            // If required, show instructions and exit app
                            if (latestInfo.Required)
                            {
                                await Application.Current.MainPage.DisplayAlert(
                                    "Installation Instructions",
                                    "1. Download the APK file\n" +
                                    "2. Tap on it when download completes\n" +
                                    "3. Tap 'Install' when prompted\n\n" +
                                    "The app will now close. Please install the update.",
                                    "OK");

                                // Exit app on Android
                                if (DeviceInfo.Platform == DevicePlatform.Android)
                                {
                                    System.Diagnostics.Process.GetCurrentProcess().Kill();
                                }
                            }
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UpdateService] Update check error: {ex.Message}");
                // Silently fail - don't disrupt app usage
            }
        }
    }

    public class AppVersionInfo
    {
        public string Version { get; set; }
        public int BuildNumber { get; set; }
        public string Notes { get; set; }
        public string DownloadUrl { get; set; }
        public bool Required { get; set; }
        public DateTime ReleaseDate { get; set; }
    }
}