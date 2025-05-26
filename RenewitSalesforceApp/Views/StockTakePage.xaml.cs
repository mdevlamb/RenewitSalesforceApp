using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Networking;
using RenewitSalesforceApp.Models;
using RenewitSalesforceApp.Services;
using System.Globalization;
using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;
using System.Linq;

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

        // Required field properties
        public bool DiscRegRequired { get; set; } = true;
        public bool YardRequired { get; set; } = true;
        public bool YardLocationRequired { get; set; } = true;

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

            // Set binding context for connectivity and required fields
            BindingContext = this;

            InitializePage();
        }

        private async void InitializePage()
        {
            try
            {
                Console.WriteLine("[StockTakePage] Initializing page");

                // Load picklist values from Salesforce or fallback to offline
                await LoadPicklistValues();

                // Check initial network status
                IsOfflineMode = Connectivity.NetworkAccess != NetworkAccess.Internet;

                // Subscribe to connectivity changes
                Connectivity.ConnectivityChanged += Connectivity_ConnectivityChanged;

                // Request location immediately when page opens
                Console.WriteLine("[StockTakePage] Requesting location services immediately");
                await GetCurrentLocationAsync();

                Console.WriteLine("[StockTakePage] Page initialized successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StockTakePage] Error initializing page: {ex.Message}");
            }
        }

        private async Task LoadPicklistValues()
        {
            try
            {
                // Try to load from Salesforce first
                if (Connectivity.NetworkAccess == NetworkAccess.Internet)
                {
                    Console.WriteLine("[StockTakePage] Loading picklist values from Salesforce");

                    var yardPicklistValues = await _salesforceService.GetPicklistValues("Yard_Stock_Take__c", "Yards__c");
                    var yardLocationPicklistValues = await _salesforceService.GetPicklistValues("Yard_Stock_Take__c", "Yard_Location__c");

                    if (yardPicklistValues?.Count > 0)
                    {
                        YardPicker.Items.Clear();
                        foreach (var value in yardPicklistValues)
                        {
                            YardPicker.Items.Add(value);
                        }
                        Console.WriteLine($"[StockTakePage] Loaded {yardPicklistValues.Count} yard values from Salesforce");
                    }
                    else
                    {
                        LoadOfflineYardValues();
                    }

                    if (yardLocationPicklistValues?.Count > 0)
                    {
                        YardLocationPicker.Items.Clear();
                        foreach (var value in yardLocationPicklistValues)
                        {
                            YardLocationPicker.Items.Add(value);
                        }
                        Console.WriteLine($"[StockTakePage] Loaded {yardLocationPicklistValues.Count} yard location values from Salesforce");
                    }
                    else
                    {
                        LoadOfflineYardLocationValues();
                    }
                }
                else
                {
                    Console.WriteLine("[StockTakePage] No internet connection, loading offline picklist values");
                    LoadOfflineYardValues();
                    LoadOfflineYardLocationValues();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StockTakePage] Error loading picklist values from Salesforce: {ex.Message}");
                // Fallback to offline values
                LoadOfflineYardValues();
                LoadOfflineYardLocationValues();
            }
        }

        private void LoadOfflineYardValues()
        {
            Console.WriteLine("[StockTakePage] Loading offline yard values");
            YardPicker.Items.Clear();
            foreach (var yardName in YardNames.All)
            {
                YardPicker.Items.Add(yardName);
            }
        }

        private void LoadOfflineYardLocationValues()
        {
            Console.WriteLine("[StockTakePage] Loading offline yard location values");
            YardLocationPicker.Items.Clear();
            foreach (var location in YardLocations.All)
            {
                YardLocationPicker.Items.Add(location);
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

        // FIXED: Single barcode scanning method using TaskCompletionSource pattern
        private async void OnLicenseDiskScanClicked(object sender, TappedEventArgs e)
        {
            try
            {
                Console.WriteLine("[StockTakePage] License disk scan button clicked");

                // Check camera permission
                var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
                if (status != PermissionStatus.Granted)
                {
                    status = await Permissions.RequestAsync<Permissions.Camera>();
                    if (status != PermissionStatus.Granted)
                    {
                        await DisplayAlert("Permission Denied", "Camera permission is required to scan barcodes.", "OK");
                        return;
                    }
                }

                var barcodeReader = new CameraBarcodeReaderView
                {
                    Options = new BarcodeReaderOptions
                    {
                        Formats = BarcodeFormats.All,
                        AutoRotate = true,
                        Multiple = false
                    }
                };

                var tcs = new TaskCompletionSource<string>();
                bool hasScanned = false; // Prevent multiple scans

                // Event handler for barcode detection
                barcodeReader.BarcodesDetected += async (s, args) =>
                {
                    if (hasScanned) return; // Prevent multiple triggers

                    if (args.Results?.Any() == true)
                    {
                        var barcode = args.Results.FirstOrDefault();
                        if (barcode != null && !string.IsNullOrEmpty(barcode.Value))
                        {
                            hasScanned = true;

                            // SUCCESS FEEDBACK: Vibration + Sound
                            try
                            {
                                // Vibrate for 200ms
                                Vibration.Vibrate(TimeSpan.FromMilliseconds(200));

                                // Play system sound (if available)
                                await PlayScanSuccessSound();
                            }
                            catch (Exception feedbackEx)
                            {
                                Console.WriteLine($"[StockTakePage] Feedback error: {feedbackEx.Message}");
                            }

                            MainThread.BeginInvokeOnMainThread(() =>
                            {
                                tcs.TrySetResult(barcode.Value);
                            });
                        }
                    }
                };

                var cancelButton = new Button
                {
                    Text = "Cancel",
                    HorizontalOptions = LayoutOptions.Center,
                    Margin = new Thickness(0, 15, 0, 0),
                    BackgroundColor = Color.FromArgb("#6c757d"),
                    TextColor = Colors.White,
                    CornerRadius = 8,
                    Padding = new Thickness(30, 10)
                };

                cancelButton.Clicked += (s, e) =>
                {
                    hasScanned = true; // Prevent scan after cancel
                    tcs.TrySetResult(null);
                };

                // BIGGER SCANNING WINDOW with instructions
                var stackLayout = new VerticalStackLayout
                {
                    Padding = new Thickness(15),
                    Spacing = 15,
                    Children =
            {
                new Label
                {
                    Text = "Scan License Disk Barcode",
                    FontAttributes = FontAttributes.Bold,
                    FontSize = 18,
                    HorizontalOptions = LayoutOptions.Center,
                    TextColor = Application.Current.RequestedTheme == AppTheme.Dark ? Colors.White : Colors.Black
                },
                new Label
                {
                    Text = "Position the barcode within the frame",
                    FontSize = 14,
                    HorizontalOptions = LayoutOptions.Center,
                    TextColor = Application.Current.RequestedTheme == AppTheme.Dark ? Color.FromArgb("#aaaaaa") : Color.FromArgb("#666666"),
                    Margin = new Thickness(0, 0, 0, 10)
                },
                new Frame
                {
                    Content = barcodeReader,
                    Padding = new Thickness(0),
                    CornerRadius = 15,
                    IsClippedToBounds = true,
                    HeightRequest = 400, // BIGGER: Increased from 300 to 400
                    WidthRequest = 350,  // Set width for better aspect ratio
                    HorizontalOptions = LayoutOptions.Center,
                    BorderColor = Color.FromArgb("#007AFF"),
                    HasShadow = true
                },
                cancelButton
            }
                };

                var scanPage = new ContentPage
                {
                    Title = "Scan License Disk",
                    Content = new ScrollView
                    {
                        Content = stackLayout,
                        VerticalOptions = LayoutOptions.Center
                    },
                    // NORMAL BACKGROUND: Removed Colors.Black, using system default
                    BackgroundColor = Application.Current.RequestedTheme == AppTheme.Dark
                        ? Color.FromArgb("#1e1e1e")
                        : Color.FromArgb("#f8f9fa")
                };

                await Navigation.PushModalAsync(scanPage);

                // Wait for scan result
                var result = await tcs.Task;

                // AUTOMATIC CLOSE: Goes back immediately after scan
                await Navigation.PopModalAsync();

                if (!string.IsNullOrEmpty(result))
                {
                    Console.WriteLine($"[StockTakePage] Barcode scanned: {result}");

                    // Show brief success message before parsing
                    MainThread.BeginInvokeOnMainThread(async () =>
                    {
                        // Optional: Show a brief toast-like message
                        await DisplayAlert("Scan Complete", "License disk scanned successfully!", "OK");
                        ParseLicenseDiskBarcode(result);
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StockTakePage] Error in license disk scan: {ex.Message}");
                await DisplayAlert("Error", "Failed to scan barcode", "OK");
            }
        }

        // Helper method for scan success sound
        private async Task PlayScanSuccessSound()
        {
            try
            {
                // Option 1: Try to play system notification sound
#if ANDROID
        await PlayAndroidNotificationSound();
#elif IOS
                await PlayiOSSystemSound();
#endif
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StockTakePage] Could not play sound: {ex.Message}");
                // Sound is optional, don't throw
            }
        }

#if ANDROID
private async Task PlayAndroidNotificationSound()
{
    try
    {
        // Use Android's notification sound
        var context = Platform.CurrentActivity ?? Android.App.Application.Context;
        var notification = Android.Media.RingtoneManager.GetDefaultUri(Android.Media.RingtoneType.Notification);
        var ringtone = Android.Media.RingtoneManager.GetRingtone(context, notification);
        ringtone?.Play();
        
        await Task.Delay(100); // Brief delay
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Android sound error: {ex.Message}");
    }
}
#endif

#if IOS
        private async Task PlayiOSSystemSound()
        {
            try
            {
                // Use iOS system sound
                AudioToolbox.SystemSound.FromFile("/System/Library/Audio/UISounds/payment_success.caf")?.PlaySystemSound();
                await Task.Delay(100);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"iOS sound error: {ex.Message}");
            }
        }
#endif

        /// <summary>
        /// Parses license disk barcode data and populates form fields
        /// Example format: %MVL1CC57%0156%4024O03C%1%40240486F3MD%KN87PFGP%HTS977K%Hatch back / Luikrug%VOLKSWAGEN%VW 216 T-CROSS%Black / Swart%WVGZZZC1ZLY056933%DKJ048733%2025-05-31%
        /// </summary>
        private void ParseLicenseDiskBarcode(string barcodeData)
        {
            try
            {
                Console.WriteLine($"[StockTakePage] Parsing barcode data: {barcodeData}");

                // Save the full barcode data to DISC_REG field
                DiscRegEntry.Text = barcodeData;

                // Split by % to get individual components
                var parts = barcodeData.Split('%');

                Console.WriteLine($"[StockTakePage] Barcode split into {parts.Length} parts");

                if (parts.Length >= 15) // Need at least 15 parts (0-14)
                {
                    // CORRECTED indices based on actual barcode structure
                    var licenseNumber = parts.Length > 5 ? parts[5] : "";     // Index 5: License number
                    var vehicleReg = parts.Length > 6 ? parts[6] : "";        // Index 6: Vehicle registration  
                    var vehicleType = parts.Length > 8 ? parts[8] : "";       // Index 8: Vehicle type
                    var make = parts.Length > 9 ? parts[9] : "";              // Index 9: Make
                    var model = parts.Length > 10 ? parts[10] : "";           // Index 10: Model
                    var colour = parts.Length > 11 ? parts[11] : "";          // Index 11: Colour
                    var vin = parts.Length > 12 ? parts[12] : "";             // Index 12: VIN
                    var engineNumber = parts.Length > 13 ? parts[13] : "";    // Index 13: Engine number
                    var expiryDate = parts.Length > 14 ? parts[14] : "";      // Index 14: Expiry date

                    // Debug logging
                    Console.WriteLine($"[StockTakePage] Extracted values:");
                    Console.WriteLine($"  License Number: {licenseNumber}");
                    Console.WriteLine($"  Vehicle Reg: {vehicleReg}");
                    Console.WriteLine($"  Vehicle Type: {vehicleType}");
                    Console.WriteLine($"  Make: {make}");
                    Console.WriteLine($"  Model: {model}");
                    Console.WriteLine($"  Colour: {colour}");
                    Console.WriteLine($"  VIN: {vin}");
                    Console.WriteLine($"  Engine Number: {engineNumber}");
                    Console.WriteLine($"  Expiry Date: {expiryDate}");

                    // Populate form fields
                    if (!string.IsNullOrEmpty(vehicleReg))
                        VehicleRegEntry.Text = vehicleReg;

                    if (!string.IsNullOrEmpty(licenseNumber))
                        LicenseEntry.Text = licenseNumber;

                    if (!string.IsNullOrEmpty(make))
                        MakeEntry.Text = make;

                    if (!string.IsNullOrEmpty(model))
                        ModelEntry.Text = model;

                    if (!string.IsNullOrEmpty(colour))
                        ColourEntry.Text = colour;

                    if (!string.IsNullOrEmpty(vehicleType))
                        VehicleTypeEntry.Text = vehicleType;

                    if (!string.IsNullOrEmpty(vin))
                        VinEntry.Text = vin;

                    if (!string.IsNullOrEmpty(engineNumber))
                        EngineEntry.Text = engineNumber;

                    if (!string.IsNullOrEmpty(expiryDate))
                        ExpiryDateEntry.Text = expiryDate;

                    Console.WriteLine($"[StockTakePage] Successfully parsed barcode - Registration: {vehicleReg}, Make: {make}, Model: {model}");

                    MainThread.BeginInvokeOnMainThread(async () =>
                    {
                        await DisplayAlert("Scan Complete",
                            $"Successfully scanned license disk!\n\nRegistration: {vehicleReg}\nMake: {make}\nModel: {model}",
                            "OK");
                    });
                }
                else
                {
                    Console.WriteLine($"[StockTakePage] Barcode format not recognized - {parts.Length} parts found, expected at least 15");

                    // Log all parts for debugging
                    for (int i = 0; i < parts.Length; i++)
                    {
                        Console.WriteLine($"[StockTakePage] Part {i}: '{parts[i]}'");
                    }

                    MainThread.BeginInvokeOnMainThread(async () =>
                    {
                        await DisplayAlert("Scan Warning",
                            $"Barcode scanned but format not recognized ({parts.Length} parts found, expected 15+). Full data saved to DISC_REG field.",
                            "OK");
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StockTakePage] Error parsing barcode: {ex.Message}");
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await DisplayAlert("Parse Error",
                        "Error parsing barcode data. Full data saved to DISC_REG field.",
                        "OK");
                });
            }
        }

        private async void OnGetLocationClicked(object sender, EventArgs e)
        {
            await GetCurrentLocationAsync();
        }

        // FIXED: Use correct Geolocation.GetLocationAsync method
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
                        GpsLabel.Text = "Location permission denied - tap to retry";
                    });
                    return;
                }

                // Get current location - FIXED: Use GetLocationAsync instead of GetCurrentLocationAsync
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
                        GpsLabel.Text = "Unable to get location - tap to retry";
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
                    await DisplayAlert("Incomplete Data", "Please fill in all required fields:\n• Vehicle Registration\n• Yard\n• Yard Location", "OK");
                    return;
                }

                // Get current location - FIXED: Use GetLocationAsync
                Location currentLocation = null;
                try
                {
                    currentLocation = await Geolocation.GetLocationAsync(new GeolocationRequest
                    {
                        DesiredAccuracy = GeolocationAccuracy.Medium,
                        Timeout = TimeSpan.FromSeconds(10)
                    });
                }
                catch (Exception locEx)
                {
                    Console.WriteLine($"[StockTakePage] Could not get location: {locEx.Message}");
                }

                // Get StockTakeService from DI
                var stockTakeService = Handler?.MauiContext?.Services?.GetService<StockTakeService>();
                if (stockTakeService == null)
                {
                    await DisplayAlert("Error", "StockTakeService not available", "OK");
                    return;
                }

                // Create stock take record
                var stockTake = await stockTakeService.CreateStockTakeAsync(
                    vehicleRegistration: VehicleRegEntry.Text,
                    discRegData: DiscRegEntry.Text,
                    yards: YardPicker.SelectedIndex >= 0 ? YardPicker.Items[YardPicker.SelectedIndex] : null,
                    yardLocation: YardLocationPicker.SelectedIndex >= 0 ? YardLocationPicker.Items[YardLocationPicker.SelectedIndex] : null,
                    photoPaths: _photoPaths,
                    comments: CommentsEditor.Text,
                    latitude: currentLocation?.Latitude,
                    longitude: currentLocation?.Longitude
                );

                Console.WriteLine($"[StockTakePage] Stock take created with LocalId: {stockTake.LocalId}");

                // Try to sync immediately if online
                if (Connectivity.NetworkAccess == NetworkAccess.Internet)
                {
                    try
                    {
                        Console.WriteLine("[StockTakePage] Attempting immediate sync");
                        var syncCount = await stockTakeService.SyncStockTakesAsync();

                        if (syncCount > 0)
                        {
                            await DisplayAlert("Success", "Stock take saved and synced to Salesforce!", "OK");
                        }
                        else
                        {
                            await DisplayAlert("Saved Locally", "Stock take saved locally. Will sync when connection improves.", "OK");
                        }
                    }
                    catch (Exception syncEx)
                    {
                        Console.WriteLine($"[StockTakePage] Sync error: {syncEx.Message}");
                        await DisplayAlert("Saved Locally", "Stock take saved locally. Will sync when connection improves.", "OK");
                    }
                }
                else
                {
                    await DisplayAlert("Saved Locally", "Stock take saved locally. Will sync when online.", "OK");
                }

                await Navigation.PopAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StockTakePage] Error submitting: {ex.Message}");
                await DisplayAlert("Error", $"Could not save stock take: {ex.Message}", "OK");
            }
        }

        private bool ValidateData()
        {
            // Check if vehicle registration is filled (required field)
            bool hasVehicleReg = !string.IsNullOrEmpty(VehicleRegEntry.Text);

            // Check if yard and yard location are selected
            bool hasLocation = YardPicker.SelectedIndex >= 0 &&
                              YardLocationPicker.SelectedIndex >= 0;

            return hasVehicleReg && hasLocation;
        }
    }
}