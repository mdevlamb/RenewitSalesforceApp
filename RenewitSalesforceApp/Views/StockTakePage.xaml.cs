using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Networking;
using RenewitSalesforceApp.Models;
using RenewitSalesforceApp.Services;

namespace RenewitSalesforceApp.Views
{
    public partial class StockTakePage : ContentPage, INotifyPropertyChanged
    {
        private readonly AuthService _authService;
        private readonly LocalDatabaseService _databaseService;
        private readonly SalesforceService _salesforceService;

        private List<string> _photoPaths = new List<string>();
        private Location _currentLocation;
        private bool _isOfflineMode;

        public bool IsOfflineMode
        {
            get => _isOfflineMode;
            set
            {
                if (_isOfflineMode != value)
                {
                    _isOfflineMode = value;
                    OnPropertyChanged();
                }
            }
        }

        // INotifyPropertyChanged implementation
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public StockTakePage(AuthService authService, LocalDatabaseService databaseService, SalesforceService salesforceService)
        {
            InitializeComponent();

            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
            _salesforceService = salesforceService ?? throw new ArgumentNullException(nameof(salesforceService));

            // Set binding context for connectivity
            BindingContext = this;

            InitializePage();
        }

        private void InitializePage()
        {
            try
            {
                Console.WriteLine("[StockTakePage] Initializing page");

                // Populate Branch picker
                foreach (var branchName in YardNames.All)
                {
                    BranchPicker.Items.Add(branchName);
                }

                // Populate Department picker
                foreach (var department in YardLocations.All)
                {
                    DepartmentPicker.Items.Add(department);
                }

                // Check initial network status
                IsOfflineMode = Connectivity.NetworkAccess != NetworkAccess.Internet;

                // Subscribe to connectivity changes
                Connectivity.ConnectivityChanged += Connectivity_ConnectivityChanged;

                // Get current location on load
                _ = Task.Run(async () => await GetCurrentLocationAsync());

                Console.WriteLine("[StockTakePage] Page initialized successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StockTakePage] Error initializing page: {ex.Message}");
            }
        }

        private void Connectivity_ConnectivityChanged(object sender, ConnectivityChangedEventArgs e)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                IsOfflineMode = e.NetworkAccess != NetworkAccess.Internet;
            });
        }

        protected override void OnDisappearing()
        {
            try
            {
                base.OnDisappearing();
                // Unsubscribe from connectivity events
                Connectivity.ConnectivityChanged -= Connectivity_ConnectivityChanged;
                Console.WriteLine("[StockTakePage] OnDisappearing called");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StockTakePage] Error in OnDisappearing: {ex.Message}");
            }
        }

        private async void OnLicenseDiskScanClicked(object sender, TappedEventArgs e)
        {
            try
            {
                Console.WriteLine("[StockTakePage] License disk scan button clicked");

                // TODO: Implement license disk OCR scanning
                await DisplayAlert(
                    "License Disk Scanner",
                    "License disk OCR scanning will be implemented here.\n\nThis will:\n• Scan vehicle license disks using camera\n• Extract registration number using OCR\n• Auto-populate relevant fields\n• Extract expiry date information",
                    "OK");

                // Simulate OCR result for now
                string result = await DisplayPromptAsync(
                    "Simulated License Disk Scan",
                    "Enter scanned data (simulating OCR):",
                    placeholder: "%MVL1CC44%0134%4024T0P9%1%4024047PMTCD%DM77PKGP%GDM915K%...");

                if (!string.IsNullOrEmpty(result))
                {
                    // Extract the clean registration number from license disk data
                    string cleanReg = ExtractRegistrationNumber(result);
                    DiscRegEntry.Text = cleanReg;
                    LicenseEntry.Text = cleanReg;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StockTakePage] Error in license disk scan: {ex.Message}");
                await DisplayAlert("Error", "Failed to scan license disk", "OK");
            }
        }

        /// <summary>
        /// Extracts clean registration number from license disk barcode data
        /// Equivalent to Salesforce formula: UPPER(IF(LEFT(MID(DISC_REG__c,40,1000),FIND("%",MID(DISC_REG__c,40,1000),1)-1) = "", DISC_REG__c, LEFT(MID(DISC_REG__c,40,1000),FIND("%",MID(DISC_REG__c,40,1000),1)-1)))
        /// </summary>
        private static string ExtractRegistrationNumber(string discReg)
        {
            if (string.IsNullOrEmpty(discReg) || discReg.Length < 40)
                return discReg?.ToUpper() ?? "";

            try
            {
                // Get substring starting from position 40 (0-based index = 39)
                string substring = discReg.Substring(39);

                // Find first % character
                int percentIndex = substring.IndexOf('%');

                if (percentIndex > 0)
                {
                    // Extract text before the first %
                    string extracted = substring.Substring(0, percentIndex);
                    return string.IsNullOrEmpty(extracted) ? discReg.ToUpper() : extracted.ToUpper();
                }

                return discReg.ToUpper();
            }
            catch
            {
                return discReg?.ToUpper() ?? "";
            }
        }

        private async void OnQRScanClicked(object sender, TappedEventArgs e)
        {
            try
            {
                Console.WriteLine("[StockTakePage] QR scan button clicked");

                // TODO: Implement QR code scanning
                await DisplayAlert(
                    "QR Code Scanner",
                    "QR code scanning will be implemented here.\n\nThis will:\n• Scan QR codes using camera\n• Parse structured data\n• Auto-populate multiple fields\n• Support various QR formats",
                    "OK");

                // Simulate QR scan result
                string qrData = await DisplayPromptAsync(
                    "Simulated QR Scan",
                    "Enter QR data (simulating scan):",
                    placeholder: "REF:ABC123|LOC:Topyard|DISC:ABC123GP");

                if (!string.IsNullOrEmpty(qrData))
                {
                    ParseQRData(qrData);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StockTakePage] Error in QR scan: {ex.Message}");
                await DisplayAlert("Error", "Failed to scan QR code", "OK");
            }
        }

        private void ParseQRData(string qrData)
        {
            try
            {
                // Simple QR data parser - can be enhanced based on actual QR format
                var parts = qrData.Split('|');
                foreach (var part in parts)
                {
                    var keyValue = part.Split(':');
                    if (keyValue.Length == 2)
                    {
                        string key = keyValue[0].ToUpper();
                        string value = keyValue[1];

                        switch (key)
                        {
                            case "REF":
                                RefIdEntry.Text = value;
                                break;
                            case "DISC":
                                DiscRegEntry.Text = value;
                                break;
                            case "LIC":
                                LicenseEntry.Text = value;
                                break;
                            case "LOC":
                                // Try to match location
                                for (int i = 0; i < DepartmentPicker.Items.Count; i++)
                                {
                                    if (DepartmentPicker.Items[i].Contains(value))
                                    {
                                        DepartmentPicker.SelectedIndex = i;
                                        break;
                                    }
                                }
                                break;
                        }
                    }
                }
                Console.WriteLine($"[StockTakePage] Parsed QR data: {qrData}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StockTakePage] Error parsing QR data: {ex.Message}");
            }
        }

        private async void OnGetLocationClicked(object sender, EventArgs e)
        {
            await GetCurrentLocationAsync();
        }

        private async Task GetCurrentLocationAsync()
        {
            try
            {
                Console.WriteLine("[StockTakePage] Getting current location");

                // Update UI to show loading
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    GpsLabel.Text = "Getting location...";
                });

                // Request location permission
                var status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
                if (status != PermissionStatus.Granted)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        GpsLabel.Text = "Location permission denied";
                    });
                    return;
                }

                // Get current location
                var request = new GeolocationRequest
                {
                    DesiredAccuracy = GeolocationAccuracy.Medium,
                    Timeout = TimeSpan.FromSeconds(10)
                };

                _currentLocation = await Geolocation.GetLocationAsync(request);

                if (_currentLocation != null)
                {
                    string coordinates = $"{_currentLocation.Latitude:F6}, {_currentLocation.Longitude:F6}";
                    Console.WriteLine($"[StockTakePage] Location obtained: {coordinates}");

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        GpsLabel.Text = coordinates;
                        MapPreviewButton.IsVisible = true; // Show map preview button
                    });
                }
                else
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        GpsLabel.Text = "Unable to get location";
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StockTakePage] Error getting location: {ex.Message}");
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    GpsLabel.Text = "Location error - tap to retry";
                });
            }
        }

        private async void OnMapPreviewClicked(object sender, EventArgs e)
        {
            try
            {
                if (_currentLocation != null)
                {
                    Console.WriteLine("[StockTakePage] Opening map preview");

                    // TODO: Implement map preview - could use Microsoft.Maui.Maps or open external maps
                    var options = new MapLaunchOptions
                    {
                        Name = "Stock Take Location"
                    };

                    await Map.OpenAsync(_currentLocation, options);
                }
                else
                {
                    await DisplayAlert("No Location", "Please get GPS coordinates first", "OK");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StockTakePage] Error opening map: {ex.Message}");
                await DisplayAlert("Map Error", "Could not open map preview", "OK");
            }
        }

        private async void OnTakePhotoClicked(object sender, EventArgs e)
        {
            try
            {
                Console.WriteLine("[StockTakePage] Take photo button clicked");

                // TODO: Implement camera functionality
                await DisplayAlert(
                    "Camera",
                    "Camera functionality will be implemented here.\n\nThis will:\n• Take photos using device camera\n• Save photos locally\n• Show photo previews\n• Allow multiple photos per stock take",
                    "OK");

                // Simulate taking a photo for UI testing
                PhotoFrame.IsVisible = true;
                NoPhotoFrame.IsVisible = false;
                _photoPaths.Add("simulated_photo_path.jpg");
                PhotoCountLabel.Text = $"{_photoPaths.Count} photo{(_photoPaths.Count > 1 ? "s" : "")}";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StockTakePage] Error taking photo: {ex.Message}");
                await DisplayAlert("Camera Error", "Could not take photo", "OK");
            }
        }

        private async void OnSubmitClicked(object sender, EventArgs e)
        {
            try
            {
                Console.WriteLine("[StockTakePage] Submit button clicked");

                if (!ValidateData())
                {
                    await DisplayAlert("Incomplete Data", "Please fill in all required fields:\n• At least one identifier\n• Branch\n• Department", "OK");
                    return;
                }

                var stockTakeRecord = CreateStockTakeRecord();
                await _databaseService.SaveStockTakeRecordAsync(stockTakeRecord);

                await DisplayAlert("Stock Take Submitted", "Stock take saved successfully and will be synced to Salesforce.", "OK");
                await Navigation.PopAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StockTakePage] Error submitting stock take: {ex.Message}");
                await DisplayAlert("Submit Error", "Could not submit stock take", "OK");
            }
        }

        private bool ValidateData()
        {
            // Check if at least one identifier is filled
            bool hasIdentifier = !string.IsNullOrEmpty(DiscRegEntry.Text) ||
                                !string.IsNullOrEmpty(LicenseEntry.Text) ||
                                !string.IsNullOrEmpty(RefIdEntry.Text);

            // Check if branch and department are selected
            bool hasLocation = BranchPicker.SelectedIndex >= 0 &&
                              DepartmentPicker.SelectedIndex >= 0;

            return hasIdentifier && hasLocation;
        }

        private StockTakeRecord CreateStockTakeRecord()
        {
            return new StockTakeRecord
            {
                DISC_REG__c = DiscRegEntry.Text,
                License_Number__c = LicenseEntry.Text,
                REFID__c = RefIdEntry.Text,
                Yard_Name__c = BranchPicker.SelectedIndex >= 0 ? BranchPicker.Items[BranchPicker.SelectedIndex] : null,
                Yard_Location__c = DepartmentPicker.SelectedIndex >= 0 ? DepartmentPicker.Items[DepartmentPicker.SelectedIndex] : null,
                Comments__c = CommentsEditor.Text,
                Stock_Take_Date = DateTime.Now,
                Stock_Take_By = _authService.CurrentUser?.Name,
                Has_Photo = _photoPaths.Count > 0,
                Photo_Count = _photoPaths.Count,
                PhotoPath = _photoPaths.Count > 0 ? _photoPaths[0] : null,
                AllPhotoPaths = string.Join(";", _photoPaths),
                GPS_CORD__c = _currentLocation != null ? $"{_currentLocation.Latitude},{_currentLocation.Longitude}" : null,
                Geo_Latitude__c = _currentLocation?.Latitude,
                Geo_Longitude__c = _currentLocation?.Longitude,
                IsSynced = false,
                SyncAttempts = 0
            };
        }
    }
}