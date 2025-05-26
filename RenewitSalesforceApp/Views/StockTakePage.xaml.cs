using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Networking;
using RenewitSalesforceApp.Models;
using RenewitSalesforceApp.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;

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
        public new event PropertyChangedEventHandler PropertyChanged;

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

                    var branchPicklistValues = await _salesforceService.GetPicklistValues("Yard_Stock_Take__c", "Yards__c");
                    var departmentPicklistValues = await _salesforceService.GetPicklistValues("Yard_Stock_Take__c", "Yard_Location__c");

                    if (branchPicklistValues?.Count > 0)
                    {
                        BranchPicker.ItemsSource = branchPicklistValues;
                        Console.WriteLine($"[StockTakePage] Loaded {branchPicklistValues.Count} branch values from Salesforce");
                    }
                    else
                    {
                        LoadOfflineBranchValues();
                    }

                    if (departmentPicklistValues?.Count > 0)
                    {
                        DepartmentPicker.ItemsSource = departmentPicklistValues;
                        Console.WriteLine($"[StockTakePage] Loaded {departmentPicklistValues.Count} department values from Salesforce");
                    }
                    else
                    {
                        LoadOfflineDepartmentValues();
                    }
                }
                else
                {
                    Console.WriteLine("[StockTakePage] No internet connection, loading offline picklist values");
                    LoadOfflineBranchValues();
                    LoadOfflineDepartmentValues();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StockTakePage] Error loading picklist values from Salesforce: {ex.Message}");
                // Fallback to offline values
                LoadOfflineBranchValues();
                LoadOfflineDepartmentValues();
            }
        }

        private void LoadOfflineBranchValues()
        {
            Console.WriteLine("[StockTakePage] Loading offline branch values");
            BranchPicker.ItemsSource = YardNames.All;
        }

        private void LoadOfflineDepartmentValues()
        {
            Console.WriteLine("[StockTakePage] Loading offline department values");
            DepartmentPicker.ItemsSource = YardLocations.All;
        }

        private void Connectivity_ConnectivityChanged(object sender, ConnectivityChangedEventArgs e)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                IsOfflineMode = e.NetworkAccess != NetworkAccess.Internet;
            });
        }

        #region Form Field Events

        private void OnVehicleRegChanged(object sender, TextChangedEventArgs e)
        {
            Console.WriteLine($"[StockTakePage] Vehicle registration changed: {e.NewTextValue}");
        }

        private void OnLocationFieldChanged(object sender, EventArgs e)
        {
            Console.WriteLine("[StockTakePage] Location field changed");
        }

        private void OnCommentsChanged(object sender, TextChangedEventArgs e)
        {
            Console.WriteLine($"[StockTakePage] Comments changed: {e.NewTextValue?.Length ?? 0} characters");
        }

        #endregion

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

        // Barcode scanning method using TaskCompletionSource pattern
        private async void OnLicenseDiskScanClicked(object sender, EventArgs e)
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

                // Scanning window with instructions
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
                            HeightRequest = 400,
                            WidthRequest = 350,
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
                    BackgroundColor = Application.Current.RequestedTheme == AppTheme.Dark
                        ? Color.FromArgb("#1e1e1e")
                        : Color.FromArgb("#f8f9fa")
                };

                await Navigation.PushModalAsync(scanPage);

                // Wait for scan result
                var result = await tcs.Task;

                // Close scanner page
                await Navigation.PopModalAsync();

                if (!string.IsNullOrEmpty(result))
                {
                    Console.WriteLine($"[StockTakePage] Barcode scanned: {result}");

                    // Parse the barcode data
                    ParseLicenseDiskBarcode(result);
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

                // Store the full barcode data in the disc reg field (hidden)
                DiscRegEntry.Text = barcodeData;

                // Split by % to get individual components
                var parts = barcodeData.Split('%');

                Console.WriteLine($"[StockTakePage] Barcode split into {parts.Length} parts");

                if (parts.Length >= 15) // Need at least 15 parts (0-14)
                {
                    // Extract vehicle data based on barcode structure
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
                            $"Barcode scanned but format not recognized ({parts.Length} parts found, expected 15+).",
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
                        "Error parsing barcode data.",
                        "OK");
                });
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
                        GpsLabel.Text = "Location permission denied - tap to retry";
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
            // Prevent multiple submissions
            if (!SubmitButton.IsEnabled)
                return;

            try
            {
                Console.WriteLine("[StockTakePage] Submit button clicked");

                // Disable button to prevent multiple submissions
                SubmitButton.IsEnabled = false;
                SubmitButton.Text = "Processing...";

                // 1. Validate required data
                if (!ValidateData())
                {
                    await DisplayAlert("Incomplete Data",
                        "Please fill in all required fields:\n• Vehicle Registration\n• Branch\n• Department",
                        "OK");
                    return;
                }

                // 2. Get current location
                Location currentLocation = _currentLocation;
                if (currentLocation == null)
                {
                    try
                    {
                        currentLocation = await Geolocation.GetLocationAsync(new GeolocationRequest
                        {
                            DesiredAccuracy = GeolocationAccuracy.Medium,
                            Timeout = TimeSpan.FromSeconds(5)
                        });
                    }
                    catch (Exception locEx)
                    {
                        Console.WriteLine($"[StockTakePage] Could not get location: {locEx.Message}");
                    }
                }

                // 3. Create stock take record object (but don't save to local DB yet)
                var stockTake = new StockTakeRecord();

                // Populate all fields
                stockTake.Vehicle_Registration__c = VehicleRegEntry.Text?.Trim();
                stockTake.DISC_REG__c = DiscRegEntry.Text;
                stockTake.License_Number__c = LicenseEntry.Text;
                stockTake.Make__c = MakeEntry.Text;
                stockTake.Model__c = ModelEntry.Text;
                stockTake.Colour__c = ColourEntry.Text;
                stockTake.Vehicle_Type__c = VehicleTypeEntry.Text;
                stockTake.VIN__c = VinEntry.Text;
                stockTake.Engine_Number__c = EngineEntry.Text;
                stockTake.License_Expiry_Date__c = ExpiryDateEntry.Text;
                stockTake.Yards__c = BranchPicker.SelectedIndex >= 0 ? BranchPicker.Items[BranchPicker.SelectedIndex] : null;
                stockTake.Yard_Location__c = DepartmentPicker.SelectedIndex >= 0 ? DepartmentPicker.Items[DepartmentPicker.SelectedIndex] : null;
                stockTake.Comments__c = CommentsEditor.Text;

                // Set metadata
                stockTake.GenerateRefId();
                stockTake.SetStockTakeDate(DateTime.Now);
                stockTake.SetStockTakeBy(_authService.CurrentUser?.Name ?? "Unknown User");

                if (currentLocation != null)
                {
                    stockTake.SetGPSCoordinates(currentLocation.Latitude, currentLocation.Longitude);
                }

                // Handle photos
                if (_photoPaths.Count > 0)
                {
                    stockTake.HasPhoto = true;
                    stockTake.PhotoCount = _photoPaths.Count;
                    stockTake.PhotoPath = _photoPaths.FirstOrDefault();
                    stockTake.AllPhotoPaths = string.Join(";", _photoPaths);
                }

                // 4. Try Salesforce first if online
                string salesforceId = null;
                bool syncedToSalesforce = false;

                if (!IsOfflineMode) // Online
                {
                    try
                    {
                        Console.WriteLine("[StockTakePage] Attempting direct submission to Salesforce");
                        await _salesforceService.EnsureAuthenticatedAsync();

                        salesforceId = await _salesforceService.CreateStockTakeRecord(stockTake);

                        if (!string.IsNullOrEmpty(salesforceId))
                        {
                            syncedToSalesforce = true;
                            Console.WriteLine($"[StockTakePage] Successfully submitted to Salesforce: {salesforceId}");

                            // Upload photos if available
                            if (_photoPaths.Count > 0)
                            {
                                try
                                {
                                    // Note: You'd need to implement photo upload here or call a service method
                                    Console.WriteLine($"[StockTakePage] Uploading {_photoPaths.Count} photos to Salesforce");
                                    // await UploadPhotosToSalesforceAsync(salesforceId, _photoPaths);
                                }
                                catch (Exception photoEx)
                                {
                                    Console.WriteLine($"[StockTakePage] Photo upload failed: {photoEx.Message}");
                                }
                            }
                        }
                    }
                    catch (Exception sfEx)
                    {
                        Console.WriteLine($"[StockTakePage] Salesforce submission failed: {sfEx.Message}");
                        // Will fall back to local save below
                    }
                }

                // 5. Save to local database (always save as backup/record)
                if (syncedToSalesforce)
                {
                    // Mark as already synced
                    stockTake.Id = salesforceId;
                    stockTake.IsSynced = true;
                    stockTake.SyncTimestamp = DateTime.Now;
                    stockTake.SyncAttempts = 1;
                }
                else
                {
                    // Mark as needs syncing
                    stockTake.IsSynced = false;
                    stockTake.SyncAttempts = 0;
                }

                // Save to local database
                var localId = await _databaseService.SaveStockTakeRecordAsync(stockTake);
                Console.WriteLine($"[StockTakePage] Saved to local database with LocalId: {localId}");

                // 6. Show appropriate success message
                if (syncedToSalesforce)
                {
                    await DisplayAlert("Success",
                        "Stock take submitted successfully to Salesforce!",
                        "OK");
                }
                else if (IsOfflineMode)
                {
                    await DisplayAlert("Saved Offline",
                        "Stock take saved locally. Will sync to Salesforce when connected.",
                        "OK");
                }
                else
                {
                    await DisplayAlert("Saved Locally",
                        "Stock take saved locally. Sync to Salesforce failed, will retry automatically.",
                        "OK");
                }

                // 7. Navigate back
                await Navigation.PopAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StockTakePage] Error submitting: {ex.Message}");
                await DisplayAlert("Error", $"Could not save stock take: {ex.Message}", "OK");
            }
            finally
            {
                // Reset button
                SubmitButton.Text = "Submit Stock Take";
                SubmitButton.IsEnabled = true;
            }
        }

        private bool ValidateData()
        {
            // Check if vehicle registration is filled (required field)
            bool hasVehicleReg = !string.IsNullOrEmpty(VehicleRegEntry.Text);

            // Check if branch and department are selected (required fields)
            bool hasLocation = BranchPicker.SelectedIndex >= 0 &&
                              DepartmentPicker.SelectedIndex >= 0;

            return hasVehicleReg && hasLocation;
        }
    }
}