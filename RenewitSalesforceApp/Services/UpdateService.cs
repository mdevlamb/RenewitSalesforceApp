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

                Console.WriteLine($"[UpdateService] Current version: {currentVersion} (Build {currentBuild})");

                // Add timestamp to bypass caching
                string url = $"{VERSION_URL}?t={DateTime.Now.Ticks}";
                Console.WriteLine($"[UpdateService] Checking for updates: {url}");

                string json = await _client.GetStringAsync(url);
                Console.WriteLine($"[UpdateService] Retrieved version info: {json}");

                var latestInfo = JsonSerializer.Deserialize<AppVersionInfo>(
                    json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );

                if (latestInfo == null)
                {
                    Console.WriteLine("[UpdateService] No version info available");
                    return;
                }

                // Parse version
                var latestVersion = new Version(latestInfo.Version);
                Console.WriteLine($"[UpdateService] Latest version: {latestVersion} (Build {latestInfo.BuildNumber})");

                // Check if update available
                bool updateAvailable = latestVersion > currentVersion ||
                                      (latestVersion == currentVersion &&
                                       latestInfo.BuildNumber > currentBuild);

                Console.WriteLine($"[UpdateService] Update available: {updateAvailable}");

                if (updateAvailable)
                {
                    // Use MainThread for UI operations
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        // Customize update dialog for Renew-it
                        string title = latestInfo.Required ?
                            "🚨 Required Update" : "📱 Update Available";

                        string message = $"Version {latestInfo.Version} (Build {latestInfo.BuildNumber}) is available.\n\n" +
                                       $"What's New:\n{latestInfo.Notes}\n\n" +
                                       $"Released: {latestInfo.ReleaseDate:MMM d, yyyy}";

                        string acceptButton = "Download Now";
                        string cancelButton = latestInfo.Required ? null : "Later";

                        Console.WriteLine($"[UpdateService] Showing update dialog: {title}");

                        // Show alert
                        bool download = await Application.Current.MainPage.DisplayAlert(
                            title, message, acceptButton, cancelButton);

                        if (download || latestInfo.Required)
                        {
                            Console.WriteLine($"[UpdateService] User chose to download. URL: {latestInfo.DownloadUrl}");

                            try
                            {
                                // FIXED: Force external browser to handle APK downloads properly
                                await Browser.OpenAsync(latestInfo.DownloadUrl, BrowserLaunchMode.External);
                                Console.WriteLine("[UpdateService] Successfully opened download URL in external browser");
                            }
                            catch (Exception browserEx)
                            {
                                Console.WriteLine($"[UpdateService] Error opening browser: {browserEx.Message}");

                                // Fallback: Show download URL to user
                                await Application.Current.MainPage.DisplayAlert(
                                    "Download Link",
                                    $"Please copy this link and open it in your browser:\n\n{latestInfo.DownloadUrl}",
                                    "OK");
                            }

                            // If required, show instructions and exit app
                            if (latestInfo.Required)
                            {
                                await Application.Current.MainPage.DisplayAlert(
                                    "Installation Instructions",
                                    "1. Download the APK file from your browser\n" +
                                    "2. Tap on the downloaded file when complete\n" +
                                    "3. Enable 'Install from Unknown Sources' if prompted\n" +
                                    "4. Tap 'Install' to update the app\n\n" +
                                    "The app will now close. Please install the update to continue.",
                                    "OK");

                                // Exit app on Android
                                if (DeviceInfo.Platform == DevicePlatform.Android)
                                {
                                    Console.WriteLine("[UpdateService] Closing app for required update");
                                    System.Diagnostics.Process.GetCurrentProcess().Kill();
                                }
                            }
                        }
                        else
                        {
                            Console.WriteLine("[UpdateService] User chose to update later");
                        }
                    });
                }
                else
                {
                    Console.WriteLine("[UpdateService] App is up to date");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UpdateService] Update check error: {ex.Message}");
                Console.WriteLine($"[UpdateService] Stack trace: {ex.StackTrace}");
                // Silently fail - don't disrupt app usage
            }
        }

        /// <summary>
        /// Manual update check (for testing or user-initiated checks)
        /// </summary>
        public async Task<bool> CheckForUpdatesManually()
        {
            try
            {
                await CheckForUpdates();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UpdateService] Manual update check failed: {ex.Message}");

                // Show error to user for manual checks
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await Application.Current.MainPage.DisplayAlert(
                        "Update Check Failed",
                        "Could not check for updates. Please check your internet connection and try again.",
                        "OK");
                });

                return false;
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