using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;

namespace RenewitSalesforceApp.Services
{
    public class UpdateService
    {
        private const string GITHUB_API_URL = "https://api.github.com/repos/mdevlamb/RenewitSalesforceApp/releases/latest";
        private const string VERSION_JSON_URL = "https://mdevlamb.github.io/RenewitSalesforceApp/version.json";
        private readonly HttpClient _client;
        private readonly bool _useGitHubApi;

        public UpdateService(bool useGitHubApi = true)
        {
            _useGitHubApi = useGitHubApi;
            _client = new HttpClient();
            _client.DefaultRequestHeaders.Add("User-Agent", "Renew-it-Salesforce-App/1.0");
            _client.DefaultRequestHeaders.Add("Cache-Control", "no-cache");

            // GitHub API requires Accept header
            if (_useGitHubApi)
            {
                _client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
            }
        }

        public async Task CheckForUpdates()
        {
            try
            {
                // Get current app version and build
                var currentVersion = new Version(AppInfo.VersionString);
                int currentBuild = int.Parse(AppInfo.BuildString);

                Console.WriteLine($"[UpdateService] Current version: {currentVersion} (Build {currentBuild})");

                AppVersionInfo latestInfo = null;

                if (_useGitHubApi)
                {
                    latestInfo = await GetVersionFromGitHubApi();
                }
                else
                {
                    latestInfo = await GetVersionFromJson();
                }

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
                    await ShowUpdateDialog(latestInfo);
                }
                else
                {
                    Console.WriteLine("[UpdateService] App is up to date");
                }
            }
            catch (HttpRequestException httpEx)
            {
                Console.WriteLine($"[UpdateService] Network error: {httpEx.Message}");
                // Silently fail for automatic checks
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UpdateService] Update check error: {ex.Message}");
                Console.WriteLine($"[UpdateService] Stack trace: {ex.StackTrace}");
                // Silently fail - don't disrupt app usage
            }
        }

        private async Task<AppVersionInfo> GetVersionFromGitHubApi()
        {
            try
            {
                Console.WriteLine($"[UpdateService] Checking GitHub API: {GITHUB_API_URL}");

                var response = await _client.GetAsync(GITHUB_API_URL);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[UpdateService] GitHub API response received");

                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    // Parse tag name (e.g., "v1.0-123")
                    var tagName = root.GetProperty("tag_name").GetString();
                    var match = Regex.Match(tagName, @"v([\d.]+)-(\d+)");

                    if (!match.Success)
                    {
                        Console.WriteLine($"[UpdateService] Could not parse tag: {tagName}");
                        return null;
                    }

                    var version = match.Groups[1].Value;
                    var buildNumber = int.Parse(match.Groups[2].Value);

                    // Get download URL from assets
                    string downloadUrl = null;
                    if (root.TryGetProperty("assets", out var assets) && assets.GetArrayLength() > 0)
                    {
                        var firstAsset = assets[0];
                        downloadUrl = firstAsset.GetProperty("browser_download_url").GetString();
                    }

                    // Get release info
                    var releaseNotes = root.GetProperty("body").GetString() ?? "No release notes";
                    var publishedAt = root.GetProperty("published_at").GetDateTime();
                    var isPrerelease = root.GetProperty("prerelease").GetBoolean();

                    return new AppVersionInfo
                    {
                        Version = version,
                        BuildNumber = buildNumber,
                        Notes = releaseNotes,
                        DownloadUrl = downloadUrl,
                        Required = false, // You could use prerelease flag or parse from notes
                        ReleaseDate = publishedAt
                    };
                }
                else
                {
                    Console.WriteLine($"[UpdateService] GitHub API returned: {response.StatusCode}");

                    // Fallback to version.json if GitHub API fails
                    if (!_useGitHubApi)
                    {
                        return null;
                    }

                    Console.WriteLine("[UpdateService] Falling back to version.json");
                    return await GetVersionFromJson();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UpdateService] GitHub API error: {ex.Message}");

                // Fallback to version.json
                if (!_useGitHubApi)
                {
                    return null;
                }

                return await GetVersionFromJson();
            }
        }

        private async Task<AppVersionInfo> GetVersionFromJson()
        {
            try
            {
                // Add timestamp to bypass caching
                string url = $"{VERSION_JSON_URL}?t={DateTime.Now.Ticks}";
                Console.WriteLine($"[UpdateService] Checking version.json: {url}");

                string json = await _client.GetStringAsync(url);
                Console.WriteLine($"[UpdateService] Retrieved version info from JSON");

                var latestInfo = JsonSerializer.Deserialize<AppVersionInfo>(
                    json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );

                return latestInfo;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UpdateService] Version.json error: {ex.Message}");
                return null;
            }
        }

        private async Task ShowUpdateDialog(AppVersionInfo latestInfo)
        {
            // Use MainThread for UI operations
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                // Removed emojis from titles
                string title = latestInfo.Required ?
                    "Required Update" : "Update Available";

                string message = $"Version {latestInfo.Version} (Build {latestInfo.BuildNumber}) is available.\n\n" +
                               $"What's New:\n{TruncateNotes(latestInfo.Notes)}\n\n" +
                               $"Released: {latestInfo.ReleaseDate:MMM d, yyyy}";

                string acceptButton = "Download Now";
                string cancelButton = latestInfo.Required ? null : "Later";

                Console.WriteLine($"[UpdateService] Showing update dialog: {title}");

                // Show alert
                bool download = await Application.Current.MainPage.DisplayAlert(
                    title, message, acceptButton, cancelButton);

                if (download || latestInfo.Required)
                {
                    await HandleDownload(latestInfo);
                }
                else
                {
                    Console.WriteLine("[UpdateService] User chose to update later");
                }
            });
        }

        private async Task HandleDownload(AppVersionInfo latestInfo)
        {
            Console.WriteLine($"[UpdateService] User chose to download. URL: {latestInfo.DownloadUrl}");

            try
            {
                // Try direct download first (Android only)
                if (DeviceInfo.Platform == DevicePlatform.Android)
                {
                    Console.WriteLine("[UpdateService] Attempting direct APK download...");

                    bool downloadSuccess = await TryDirectDownload(latestInfo.DownloadUrl);

                    if (downloadSuccess)
                    {
                        Console.WriteLine("[UpdateService] Direct download completed successfully");
                        return;
                    }
                    else
                    {
                        Console.WriteLine("[UpdateService] Direct download failed, falling back to browser");
                    }
                }

                // Fallback to browser download
                await ShowBrowserDownloadOptions(latestInfo);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UpdateService] Download error: {ex.Message}");
                await ShowBrowserDownloadOptions(latestInfo);
            }
        }

        private async Task<bool> TryDirectDownload(string downloadUrl)
        {
            try
            {
                // Show loading indicator
                await Application.Current.MainPage.DisplayAlert(
                    "Downloading",
                    "Download starting... Please wait.",
                    "OK");

                // Download the APK file
                using var response = await _client.GetAsync(downloadUrl);
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[UpdateService] Download failed with status: {response.StatusCode}");
                    return false;
                }

                // Get the file name from the URL or headers
                string fileName = "RenewitSalesforceApp_Update.apk";
                if (response.Content.Headers.ContentDisposition?.FileName != null)
                {
                    fileName = response.Content.Headers.ContentDisposition.FileName.Trim('"');
                }

                // Save to Downloads folder (cross-platform approach)
                string downloadsPath;
                if (DeviceInfo.Platform == DevicePlatform.Android)
                {
                    // Use a public directory that doesn't require special permissions
                    downloadsPath = Path.Combine("/storage/emulated/0/Download");
                }
                else
                {
                    // Fallback for other platforms
                    downloadsPath = Path.Combine(FileSystem.AppDataDirectory, "Downloads");
                    Directory.CreateDirectory(downloadsPath);
                }
                string filePath = Path.Combine(downloadsPath, fileName);

                // Write the file
                using var fileStream = File.Create(filePath);
                await response.Content.CopyToAsync(fileStream);

                Console.WriteLine($"[UpdateService] APK downloaded to: {filePath}");

                // Show success message with install instructions
                await Application.Current.MainPage.DisplayAlert(
                    "Download Complete",
                    $"Update downloaded successfully!\n\n" +
                    $"File saved to: Downloads/{fileName}\n\n" +
                    $"To install:\n" +
                    $"1. Open your file manager or notification panel\n" +
                    $"2. Tap on the downloaded APK file\n" +
                    $"3. Enable 'Install from Unknown Sources' if prompted\n" +
                    $"4. Tap 'Install' to update the app",
                    "OK");

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UpdateService] Direct download error: {ex.Message}");
                return false;
            }
        }

        private async Task ShowBrowserDownloadOptions(AppVersionInfo latestInfo)
        {
            // Create a custom page with download options
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                string action = await Application.Current.MainPage.DisplayActionSheet(
                    "Download Options",
                    "Cancel",
                    null,
                    "Open Download Link in Browser",
                    "Copy Download Link to Clipboard");

                switch (action)
                {
                    case "Open Download Link in Browser":
                        try
                        {
                            await Browser.OpenAsync(latestInfo.DownloadUrl, BrowserLaunchMode.External);
                            Console.WriteLine("[UpdateService] Successfully opened download URL in external browser");
                        }
                        catch (Exception browserEx)
                        {
                            Console.WriteLine($"[UpdateService] Error opening browser: {browserEx.Message}");
                            await ShowDownloadError();
                        }
                        break;

                    case "Copy Download Link to Clipboard":
                        try
                        {
                            await Clipboard.SetTextAsync(latestInfo.DownloadUrl);
                            await Application.Current.MainPage.DisplayAlert(
                                "Link Copied",
                                "Download link copied to clipboard. You can now paste it in your browser.",
                                "OK");
                        }
                        catch (Exception clipEx)
                        {
                            Console.WriteLine($"[UpdateService] Error copying to clipboard: {clipEx.Message}");
                            await ShowDownloadError();
                        }
                        break;

                    default:
                        Console.WriteLine("[UpdateService] User cancelled download");
                        break;
                }
            });

            // If required, show instructions and exit app
            if (latestInfo.Required)
            {
                await Application.Current.MainPage.DisplayAlert(
                    "Installation Required",
                    "This update is required to continue using the app. " +
                    "Please download and install the update, then restart the app.",
                    "OK");

                // Exit app on Android
                if (DeviceInfo.Platform == DevicePlatform.Android)
                {
                    Console.WriteLine("[UpdateService] Closing app for required update");
                    System.Diagnostics.Process.GetCurrentProcess().Kill();
                }
            }
        }

        private async Task ShowDownloadError()
        {
            await Application.Current.MainPage.DisplayAlert(
                "Download Error",
                "Unable to download the update automatically. Please visit the app store or contact support.",
                "OK");
        }

        private string TruncateNotes(string notes)
        {
            if (string.IsNullOrEmpty(notes))
                return "No release notes available";

            // Limit release notes to reasonable length for dialog
            const int maxLength = 300;
            if (notes.Length > maxLength)
            {
                return notes.Substring(0, maxLength) + "...";
            }

            return notes;
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