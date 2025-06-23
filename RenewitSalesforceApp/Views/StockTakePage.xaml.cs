using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Platform;
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

        private static readonly SemaphoreSlim _cameraSemaphore = new SemaphoreSlim(1, 1);
        private CameraBarcodeReaderView _currentBarcodeReader;
        private bool _isCameraInUse = false;
        private DateTime _lastCameraUse = DateTime.MinValue;

        private Button _flashlightButton;
        private bool _isFlashlightOn = false;

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
                // Get user's allowed branches first
                var currentUser = _authService.CurrentUser;
                var userAllowedBranches = currentUser?.GetAllowedBranches() ?? new List<string>();

                Console.WriteLine($"[StockTakePage] User allowed branches: {string.Join(", ", userAllowedBranches)}");

                // Try to load from Salesforce first
                if (Connectivity.NetworkAccess == NetworkAccess.Internet)
                {
                    Console.WriteLine("[StockTakePage] Loading picklist values from Salesforce");

                    var branchPicklistValues = await _salesforceService.GetPicklistValues("Yard_Stock_Take__c", "Yards__c");
                    var departmentPicklistValues = await _salesforceService.GetPicklistValues("Yard_Stock_Take__c", "Yard_Location__c");

                    if (branchPicklistValues?.Count > 0)
                    {
                        // Filter Salesforce branches based on user permissions
                        var filteredBranches = FilterBranchesByUserPermissions(branchPicklistValues, userAllowedBranches);
                        BranchPicker.ItemsSource = filteredBranches;
                        Console.WriteLine($"[StockTakePage] Loaded {filteredBranches.Count} filtered branch values from Salesforce");
                    }
                    else
                    {
                        LoadOfflineBranchValues(userAllowedBranches);
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
                    LoadOfflineBranchValues(userAllowedBranches);
                    LoadOfflineDepartmentValues();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StockTakePage] Error loading picklist values from Salesforce: {ex.Message}");
                // Fallback to offline values
                var currentUser = _authService.CurrentUser;
                var userAllowedBranches = currentUser?.GetAllowedBranches() ?? new List<string>();
                LoadOfflineBranchValues(userAllowedBranches);
                LoadOfflineDepartmentValues();
            }
        }

        private List<string> FilterBranchesByUserPermissions(List<string> allBranches, List<string> userAllowedBranches)
        {
            try
            {
                // If user has no permissions set, show all branches (fallback)
                if (userAllowedBranches == null || userAllowedBranches.Count == 0)
                {
                    Console.WriteLine("[StockTakePage] User has no branch permissions, showing all branches");
                    return allBranches;
                }

                // Filter branches - only show those the user has permission for
                var filteredBranches = allBranches
                    .Where(branch => userAllowedBranches.Contains(branch, StringComparer.OrdinalIgnoreCase))
                    .ToList();

                Console.WriteLine($"[StockTakePage] Filtered {allBranches.Count} branches down to {filteredBranches.Count} allowed branches");

                // If no matches found (maybe permissions don't match exactly), show all as fallback
                if (filteredBranches.Count == 0)
                {
                    Console.WriteLine("[StockTakePage] Warning: No branch permissions matched, showing all branches as fallback");
                    return allBranches;
                }

                return filteredBranches;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StockTakePage] Error filtering branches: {ex.Message}");
                return allBranches; // Fallback to all branches on error
            }
        }

        private void LoadOfflineBranchValues(List<string> userAllowedBranches)
        {
            try
            {
                Console.WriteLine("[StockTakePage] Loading offline branch values");

                // If user has no permissions, show all offline branches (fallback)
                if (userAllowedBranches == null || userAllowedBranches.Count == 0)
                {
                    Console.WriteLine("[StockTakePage] User has no branch permissions, showing all offline branches");
                    BranchPicker.ItemsSource = YardNames.All;
                    return;
                }

                // Filter offline branches based on user permissions
                var filteredOfflineBranches = YardNames.All
                    .Where(branch => userAllowedBranches.Contains(branch, StringComparer.OrdinalIgnoreCase))
                    .ToList();

                Console.WriteLine($"[StockTakePage] Filtered {YardNames.All.Length} offline branches down to {filteredOfflineBranches.Count}");

                // If no matches, show all as fallback
                if (filteredOfflineBranches.Count == 0)
                {
                    Console.WriteLine("[StockTakePage] Warning: No offline branch permissions matched, showing all as fallback");
                    BranchPicker.ItemsSource = YardNames.All;
                }
                else
                {
                    BranchPicker.ItemsSource = filteredOfflineBranches;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StockTakePage] Error loading offline branches: {ex.Message}");
                BranchPicker.ItemsSource = YardNames.All; // Fallback to all
            }
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

        private async void OnLicenseDiskScanClicked(object sender, EventArgs e)
        {
            // Check if camera was recently used
            var timeSinceLastUse = DateTime.Now - _lastCameraUse;
            if (timeSinceLastUse.TotalSeconds < 2)
            {
                await DisplayAlert("Camera Busy", "Please wait a moment before using the camera again.", "OK");
                return;
            }

            // Wait for camera to be available
            if (!await _cameraSemaphore.WaitAsync(5000))
            {
                await DisplayAlert("Camera Busy", "Camera is currently in use. Please wait and try again.", "OK");
                return;
            }

            _isCameraInUse = true;
            _lastCameraUse = DateTime.Now;

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
                        Formats = BarcodeFormat.Pdf417,
                        AutoRotate = false,
                        Multiple = false
                    },
                    IsTorchOn = false
                };

                // Store reference to current barcode reader
                _currentBarcodeReader = barcodeReader;
                _isFlashlightOn = false;

                var tcs = new TaskCompletionSource<string>();
                bool hasScanned = false;

                // Event handler for barcode detection
                barcodeReader.BarcodesDetected += async (s, args) =>
                {
                    if (hasScanned) return;

                    if (args.Results?.Any() == true)
                    {
                        var barcode = args.Results.FirstOrDefault();
                        if (barcode != null && !string.IsNullOrEmpty(barcode.Value))
                        {
                            hasScanned = true;

                            // Immediately stop the camera
                            await MainThread.InvokeOnMainThreadAsync(async () =>
                            {
                                barcodeReader.IsEnabled = false;
                                await TurnOffFlashlight(); // No error!
                            });

                            // SUCCESS FEEDBACK
                            try
                            {
                                Vibration.Vibrate(TimeSpan.FromMilliseconds(200));
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

                // Create scanning page content
                var scanningOverlay = CreateScanningOverlay();

                var cameraFrame = new Frame
                {
                    Content = new Grid
                    {
                        Children = { barcodeReader, scanningOverlay }
                    },
                    Padding = new Thickness(0),
                    CornerRadius = 6,
                    IsClippedToBounds = true,
                    HeightRequest = 500,
                    WidthRequest = 380,
                    HorizontalOptions = LayoutOptions.Center,
                    BorderColor = Color.FromArgb("#007AFF"),
                    HasShadow = true
                };

                var cancelButton = new Button
                {
                    Text = "Cancel",
                    HorizontalOptions = LayoutOptions.Center,
                    Margin = new Thickness(0, 20, 0, 0),
                    BackgroundColor = Color.FromArgb("#6c757d"),
                    TextColor = Colors.White,
                    CornerRadius = 8,
                    Padding = new Thickness(40, 12),
                    FontSize = 16
                };

                cancelButton.Clicked += async (s, e) =>
                {
                    hasScanned = true;
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        barcodeReader.IsEnabled = false;
                        await TurnOffFlashlight();
                    });
                    tcs.TrySetResult(null);
                };

                var stackLayout = new VerticalStackLayout
                {
                    Padding = new Thickness(20),
                    Spacing = 20,
                    Children =
            {
                new Label
                {
                    Text = "Scan License Disk Barcode",
                    FontAttributes = FontAttributes.Bold,
                    FontSize = 22,
                    HorizontalOptions = LayoutOptions.Center,
                    TextColor = Application.Current.RequestedTheme == AppTheme.Dark ? Colors.White : Colors.Black,
                    Margin = new Thickness(0, 10, 0, 0)
                },
                new Label
                {
                    Text = "Position the barcode within the green frame\nAlign the barcode horizontally for best results",
                    FontSize = 14,
                    HorizontalOptions = LayoutOptions.Center,
                    HorizontalTextAlignment = TextAlignment.Center,
                    TextColor = Application.Current.RequestedTheme == AppTheme.Dark ? Color.FromArgb("#aaaaaa") : Color.FromArgb("#666666"),
                    Margin = new Thickness(0, 0, 0, 5)
                },
                cameraFrame,
                new Label
                {
                    Text = "💡 Hold steady and ensure good lighting",
                    FontSize = 12,
                    HorizontalOptions = LayoutOptions.Center,
                    TextColor = Color.FromArgb("#FF9800"),
                    FontAttributes = FontAttributes.Italic
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

                // Add disappearing handler to clean up
                scanPage.Disappearing += async (s, e) =>  // ← Add 'async'
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>  // ← Use InvokeOnMainThreadAsync
                    {
                        if (_currentBarcodeReader != null)
                        {
                            _currentBarcodeReader.IsEnabled = false;
                            await TurnOffFlashlight();  // ← Add flashlight cleanup
                            _currentBarcodeReader = null;
                        }
                    });
                };

                await Navigation.PushModalAsync(scanPage);

                // Wait for scan result
                var result = await tcs.Task;

                // Ensure camera is disabled before closing
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (_currentBarcodeReader != null)
                    {
                        _currentBarcodeReader.IsEnabled = false;
                    }
                });

                // Add delay before closing to ensure camera is released
                await Task.Delay(500);

                // Close scanner page
                await Navigation.PopModalAsync();

                if (!string.IsNullOrEmpty(result))
                {
                    Console.WriteLine($"[StockTakePage] Barcode scanned: {result}");
                    ParseLicenseDiskBarcode(result);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StockTakePage] Error in license disk scan: {ex.Message}");
                await DisplayAlert("Error", "Failed to scan barcode", "OK");
            }
            finally
            {
                // Clean up
                await TurnOffFlashlight();
                _currentBarcodeReader = null;
                _isCameraInUse = false;
                _lastCameraUse = DateTime.Now;
                _flashlightButton = null;
                _cameraSemaphore.Release();
                Console.WriteLine("[StockTakePage] Barcode scanner released camera resources");
            }
        }

        private Grid CreateScanningOverlay()
        {
            var overlay = new Grid
            {
                InputTransparent = false, // Changed to false to allow button interactions
                BackgroundColor = Colors.Transparent,
                RowDefinitions = new RowDefinitionCollection
        {
            new RowDefinition { Height = new GridLength(60) },                       // Top area for flashlight button
            new RowDefinition { Height = new GridLength(0.4, GridUnitType.Star) },  // Middle area 
            new RowDefinition { Height = new GridLength(120) },                      // Barcode scanning area 
            new RowDefinition { Height = new GridLength(0.6, GridUnitType.Star) }   // Bottom area
        }
            };

            // Top area with flashlight button
            var topContainer = new Grid
            {
                BackgroundColor = Color.FromArgb("#80000000"),
                Padding = new Thickness(20, 10)
            };

            var flashlightButton = new Button
            {
                Text = "\ue8f4",  // Material Icons flashlight_on
                FontFamily = "MaterialIcons",
                FontSize = 24,
                BackgroundColor = Color.FromArgb("#40FFFFFF"),
                TextColor = Colors.White,
                CornerRadius = 25,
                WidthRequest = 50,
                HeightRequest = 50,
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Center,
                BorderColor = Colors.White,
                BorderWidth = 1
            };

            // Store reference to flashlight button for updates
            _flashlightButton = flashlightButton;

            flashlightButton.Clicked += OnFlashlightToggle;
            topContainer.Children.Add(flashlightButton);
            overlay.Add(topContainer, 0, 0);

            // Middle semi-transparent overlay
            var middleOverlay = new BoxView
            {
                BackgroundColor = Color.FromArgb("#80000000"),
                InputTransparent = true
            };
            overlay.Add(middleOverlay, 0, 1);

            // Bottom semi-transparent overlay  
            var bottomOverlay = new BoxView
            {
                BackgroundColor = Color.FromArgb("#80000000"),
                InputTransparent = true
            };
            overlay.Add(bottomOverlay, 0, 3);

            // Clear barcode scanning area - positioned where the actual barcode is
            var scanningAreaContainer = new Grid
            {
                BackgroundColor = Colors.Transparent,
                Margin = new Thickness(40, 0), // Focus on barcode width
                InputTransparent = true
            };

            // Add minimal scanning guides focused on barcode area
            AddBarcodeTargetGuides(scanningAreaContainer);

            overlay.Add(scanningAreaContainer, 0, 2);

            return overlay;
        }

        private void AddBarcodeTargetGuides(Grid container)
        {
            // Minimal corner guides - just small corners to indicate barcode area
            var cornerSize = 20;        // Smaller, cleaner corners
            var cornerThickness = 3;    // Thinner lines
            var cornerOffset = 8;       // Closer to actual barcode edges

            // Only add corner brackets - no center lines or extra elements

            // Top-left corner
            var topLeftH = new BoxView
            {
                BackgroundColor = Color.FromArgb("#00FF00"),
                WidthRequest = cornerSize,
                HeightRequest = cornerThickness,
                HorizontalOptions = LayoutOptions.Start,
                VerticalOptions = LayoutOptions.Start,
                Margin = new Thickness(cornerOffset, cornerOffset, 0, 0)
            };

            var topLeftV = new BoxView
            {
                BackgroundColor = Color.FromArgb("#00FF00"),
                WidthRequest = cornerThickness,
                HeightRequest = cornerSize,
                HorizontalOptions = LayoutOptions.Start,
                VerticalOptions = LayoutOptions.Start,
                Margin = new Thickness(cornerOffset, cornerOffset, 0, 0)
            };

            // Top-right corner
            var topRightH = new BoxView
            {
                BackgroundColor = Color.FromArgb("#00FF00"),
                WidthRequest = cornerSize,
                HeightRequest = cornerThickness,
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Start,
                Margin = new Thickness(0, cornerOffset, cornerOffset, 0)
            };

            var topRightV = new BoxView
            {
                BackgroundColor = Color.FromArgb("#00FF00"),
                WidthRequest = cornerThickness,
                HeightRequest = cornerSize,
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Start,
                Margin = new Thickness(0, cornerOffset, cornerOffset, 0)
            };

            // Bottom-left corner
            var bottomLeftH = new BoxView
            {
                BackgroundColor = Color.FromArgb("#00FF00"),
                WidthRequest = cornerSize,
                HeightRequest = cornerThickness,
                HorizontalOptions = LayoutOptions.Start,
                VerticalOptions = LayoutOptions.End,
                Margin = new Thickness(cornerOffset, 0, 0, cornerOffset)
            };

            var bottomLeftV = new BoxView
            {
                BackgroundColor = Color.FromArgb("#00FF00"),
                WidthRequest = cornerThickness,
                HeightRequest = cornerSize,
                HorizontalOptions = LayoutOptions.Start,
                VerticalOptions = LayoutOptions.End,
                Margin = new Thickness(cornerOffset, 0, 0, cornerOffset)
            };

            // Bottom-right corner
            var bottomRightH = new BoxView
            {
                BackgroundColor = Color.FromArgb("#00FF00"),
                WidthRequest = cornerSize,
                HeightRequest = cornerThickness,
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.End,
                Margin = new Thickness(0, 0, cornerOffset, cornerOffset)
            };

            var bottomRightV = new BoxView
            {
                BackgroundColor = Color.FromArgb("#00FF00"),
                WidthRequest = cornerThickness,
                HeightRequest = cornerSize,
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.End,
                Margin = new Thickness(0, 0, cornerOffset, cornerOffset)
            };

            // Add only the corner guides - clean and minimal
            container.Children.Add(topLeftH);
            container.Children.Add(topLeftV);
            container.Children.Add(topRightH);
            container.Children.Add(topRightV);
            container.Children.Add(bottomLeftH);
            container.Children.Add(bottomLeftV);
            container.Children.Add(bottomRightH);
            container.Children.Add(bottomRightV);
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
                var toneGenerator = new Android.Media.ToneGenerator(Android.Media.Stream.Notification, 80);
                toneGenerator.StartTone(Android.Media.Tone.PropBeep, 200);
                await Task.Delay(200);
                toneGenerator.Release();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Modern sound error: {ex.Message}");
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

        // Add this method for flashlight toggle
        private async void OnFlashlightToggle(object sender, EventArgs e)
        {
            try
            {
                if (_currentBarcodeReader != null)
                {
                    _isFlashlightOn = !_isFlashlightOn;

                    // Toggle the torch on the camera
                    _currentBarcodeReader.IsTorchOn = _isFlashlightOn;

                    // Update button appearance
                    UpdateFlashlightButtonAppearance();

                    Console.WriteLine($"[StockTakePage] Flashlight toggled: {(_isFlashlightOn ? "ON" : "OFF")}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StockTakePage] Error toggling flashlight: {ex.Message}");
                await DisplayAlert("Flashlight Error", "Could not control flashlight", "OK");
            }
        }

        // Add this method to update flashlight button appearance
        private void UpdateFlashlightButtonAppearance()
        {
            if (_flashlightButton != null)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_isFlashlightOn)
                    {
                        _flashlightButton.Text = "\ue8f4";  // flashlight_on
                        _flashlightButton.BackgroundColor = Color.FromArgb("#FFD700");
                        _flashlightButton.TextColor = Colors.Black;
                        _flashlightButton.BorderColor = Color.FromArgb("#FFD700");
                    }
                    else
                    {
                        _flashlightButton.Text = "\ue8f5";  // flashlight_off
                        _flashlightButton.BackgroundColor = Color.FromArgb("#40FFFFFF");
                        _flashlightButton.TextColor = Colors.White;
                        _flashlightButton.BorderColor = Colors.White;
                    }
                });
            }
        }

        // Add this method to ensure flashlight is turned off
        private async Task TurnOffFlashlight()
        {
            try
            {
                if (_currentBarcodeReader != null && _isFlashlightOn)
                {
                    _currentBarcodeReader.IsTorchOn = false;
                    _isFlashlightOn = false;
                    UpdateFlashlightButtonAppearance();
                    Console.WriteLine("[StockTakePage] Flashlight turned off");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StockTakePage] Error turning off flashlight: {ex.Message}");
            }
        }

        private async void OnTakePhotoClicked(object sender, EventArgs e)
        {
            // Check if camera was recently used
            var timeSinceLastUse = DateTime.Now - _lastCameraUse;
            if (timeSinceLastUse.TotalSeconds < 2)
            {
                await DisplayAlert("Camera Busy", "Please wait a moment before using the camera again.", "OK");
                return;
            }

            // Stricter timeout for photo capture
            if (!await _cameraSemaphore.WaitAsync(3000))
            {
                await DisplayAlert("Camera Busy", "Camera is currently in use. Please wait and try again.", "OK");
                return;
            }

            _isCameraInUse = true;
            _lastCameraUse = DateTime.Now;

            try
            {
                Console.WriteLine("[StockTakePage] Take photo button clicked");

                // Check camera permission
                var cameraStatus = await Permissions.CheckStatusAsync<Permissions.Camera>();
                if (cameraStatus != PermissionStatus.Granted)
                {
                    cameraStatus = await Permissions.RequestAsync<Permissions.Camera>();
                    if (cameraStatus != PermissionStatus.Granted)
                    {
                        await DisplayAlert("Camera Permission", "Camera permission is required to take photos.", "OK");
                        return;
                    }
                }

                // Check if camera is available
                if (!MediaPicker.Default.IsCaptureSupported)
                {
                    await DisplayAlert("Camera Not Available", "Camera is not available on this device.", "OK");
                    return;
                }

                // Show loading overlay
                FullScreenLoadingOverlay.IsVisible = true;


                FileResult photo = null;

                try
                {
                    // Use a cancellation token for timeout
                    using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                    {
                        var photoOptions = new MediaPickerOptions
                        {
                            Title = "Stock Take Photo"
                        };

                        photo = await MediaPicker.Default.CapturePhotoAsync(photoOptions);
                    }
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("[StockTakePage] Camera capture timed out");
                    await DisplayAlert("Timeout", "Camera took too long to respond. Please try again.", "OK");
                    return;
                }
                catch (Exception cameraEx)
                {
                    Console.WriteLine($"[StockTakePage] Camera error: {cameraEx.Message}");

                    string errorMessage = "Failed to access camera.";
                    if (cameraEx.Message.Contains("busy") || cameraEx.Message.Contains("in use"))
                    {
                        errorMessage = "Camera is busy. Please close other camera apps and try again.";
                    }

                    await DisplayAlert("Camera Error", errorMessage, "OK");
                    return;
                }

                if (photo != null)
                {
                    Console.WriteLine($"[StockTakePage] Photo captured successfully: {photo.FileName}");

                    try
                    {
                        // Save photo logic (unchanged)
                        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                        string vehicleReg = VehicleRegEntry.Text?.Replace(" ", "").Replace("/", "") ?? "Unknown";
                        string fileName = $"StockTake_{vehicleReg}_{timestamp}_{_photoPaths.Count + 1}.jpg";

                        string localAppData = FileSystem.AppDataDirectory;
                        string photosFolder = Path.Combine(localAppData, "StockTakePhotos");
                        Directory.CreateDirectory(photosFolder);

                        string localFilePath = Path.Combine(photosFolder, fileName);

                        using (var sourceStream = await photo.OpenReadAsync())
                        {
                            using (var localFileStream = File.Create(localFilePath))
                            {
                                await sourceStream.CopyToAsync(localFileStream);
                                await localFileStream.FlushAsync();
                            }
                        }

                        if (File.Exists(localFilePath))
                        {
                            var fileInfo = new FileInfo(localFilePath);
                            Console.WriteLine($"[StockTakePage] Photo saved: {localFilePath} ({fileInfo.Length} bytes)");

                            _photoPaths.Add(localFilePath);
                            await UpdatePhotoDisplay();

                            await DisplayAlert("Photo Taken",
                                $"Photo {_photoPaths.Count} captured successfully!",
                                "Great!");
                        }
                    }
                    catch (Exception saveEx)
                    {
                        Console.WriteLine($"[StockTakePage] Error saving photo: {saveEx.Message}");
                        await DisplayAlert("Save Error", "Photo was taken but could not be saved.", "OK");
                    }
                }
                else
                {
                    Console.WriteLine("[StockTakePage] Photo capture was cancelled by user");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StockTakePage] Unexpected error: {ex.Message}");
                await DisplayAlert("Error", "An unexpected error occurred.", "OK");
            }
            finally
            {
                // Always clean up
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    FullScreenLoadingOverlay.IsVisible = false;
                });

                _isCameraInUse = false;
                _lastCameraUse = DateTime.Now;

                _cameraSemaphore.Release();
                Console.WriteLine("[StockTakePage] Camera resources released");
            }
        }

        private async Task UpdatePhotoDisplay()
        {
            try
            {
                if (_photoPaths.Count > 0)
                {
                    // Show photo frame and hide no-photo frame
                    PhotoFrame.IsVisible = true;
                    NoPhotoFrame.IsVisible = false;

                    // Update photo count
                    PhotoCountLabel.Text = $"{_photoPaths.Count} photo{(_photoPaths.Count > 1 ? "s" : "")}";

                    // Show/hide view all button based on photo count
                    ViewAllPhotosButton.IsVisible = _photoPaths.Count > 1;

                    // Load and display the most recent photo as thumbnail
                    string latestPhotoPath = _photoPaths[_photoPaths.Count - 1];

                    if (File.Exists(latestPhotoPath))
                    {
                        // Load the image for display using the PhotoPreview Image control
                        var imageSource = ImageSource.FromFile(latestPhotoPath);
                        PhotoPreview.Source = imageSource;

                        Console.WriteLine($"[StockTakePage] Updated photo display with {_photoPaths.Count} photos");
                    }
                }
                else
                {
                    // No photos - show no-photo frame
                    PhotoFrame.IsVisible = false;
                    NoPhotoFrame.IsVisible = true;
                    PhotoCountLabel.Text = "No photos";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StockTakePage] Error updating photo display: {ex.Message}");
            }
        }

        private async void OnViewPhotosClicked(object sender, EventArgs e)
        {
            try
            {
                if (_photoPaths.Count == 0)
                {
                    await DisplayAlert("No Photos", "No photos have been taken yet.", "OK");
                    return;
                }

                // Create a simple photo selection dialog
                var actions = new List<string>();
                for (int i = 0; i < _photoPaths.Count; i++)
                {
                    actions.Add($"View Photo {i + 1}");
                }
                actions.Add("Delete All Photos");

                string action = await DisplayActionSheet(
                    $"Photos ({_photoPaths.Count})",
                    "Cancel",
                    null,
                    actions.ToArray());

                if (action == "Delete All Photos")
                {
                    bool confirm = await DisplayAlert("Delete All Photos",
                        $"Are you sure you want to delete all {_photoPaths.Count} photos?",
                        "Delete", "Cancel");

                    if (confirm)
                    {
                        await DeleteAllPhotos();
                    }
                }
                else if (action != null && action.StartsWith("View Photo"))
                {
                    // Extract photo number
                    if (int.TryParse(action.Replace("View Photo ", ""), out int photoNum) &&
                        photoNum <= _photoPaths.Count)
                    {
                        await ShowPhotoViewer(photoNum - 1);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StockTakePage] Error viewing photos: {ex.Message}");
                await DisplayAlert("Error", "Could not view photos", "OK");
            }
        }

        private async Task ShowPhotoViewer(int photoIndex)
        {
            try
            {
                if (photoIndex >= 0 && photoIndex < _photoPaths.Count)
                {
                    string photoPath = _photoPaths[photoIndex];
                    string fileName = Path.GetFileName(photoPath);

                    if (!File.Exists(photoPath))
                    {
                        await DisplayAlert("Photo Not Found", "The photo file could not be found.", "OK");
                        return;
                    }

                    // Create a proper photo viewer page
                    var photoImage = new Image
                    {
                        Source = ImageSource.FromFile(photoPath),
                        Aspect = Aspect.AspectFit,
                        BackgroundColor = Colors.Black,
                        HorizontalOptions = LayoutOptions.FillAndExpand,
                        VerticalOptions = LayoutOptions.FillAndExpand
                    };

                    var deleteButton = new Button
                    {
                        Text = "Delete This Photo",
                        BackgroundColor = Color.FromArgb("#dc3545"),
                        TextColor = Colors.White,
                        CornerRadius = 8,
                        Margin = new Thickness(20, 10),
                        HorizontalOptions = LayoutOptions.Center
                    };

                    var closeButton = new Button
                    {
                        Text = "Close",
                        BackgroundColor = Color.FromArgb("#6c757d"),
                        TextColor = Colors.White,
                        CornerRadius = 8,
                        Margin = new Thickness(20, 10),
                        HorizontalOptions = LayoutOptions.Center
                    };

                    var infoLabel = new Label
                    {
                        Text = $"Photo {photoIndex + 1} of {_photoPaths.Count}\n{fileName}",
                        TextColor = Colors.White,
                        FontSize = 14,
                        HorizontalOptions = LayoutOptions.Center,
                        HorizontalTextAlignment = TextAlignment.Center,
                        Margin = new Thickness(20, 10)
                    };

                    var buttonStack = new HorizontalStackLayout
                    {
                        HorizontalOptions = LayoutOptions.Center,
                        Spacing = 15,
                        Children = { deleteButton, closeButton }
                    };

                    var content = new StackLayout
                    {
                        BackgroundColor = Colors.Black,
                        Children =
                {
                    new StackLayout
                    {
                        VerticalOptions = LayoutOptions.FillAndExpand,
                        Children = { photoImage }
                    },
                    infoLabel,
                    buttonStack
                }
                    };

                    var photoViewerPage = new ContentPage
                    {
                        Title = $"Photo {photoIndex + 1}",
                        Content = content,
                        BackgroundColor = Colors.Black
                    };

                    var tcs = new TaskCompletionSource<string>();

                    deleteButton.Clicked += async (s, e) =>
                    {
                        bool confirm = await DisplayAlert("Delete Photo",
                            "Are you sure you want to delete this photo?",
                            "Delete", "Cancel");

                        if (confirm)
                        {
                            tcs.SetResult("delete");
                        }
                    };

                    closeButton.Clicked += (s, e) => tcs.SetResult("close");

                    await Navigation.PushModalAsync(photoViewerPage);

                    var result = await tcs.Task;

                    await Navigation.PopModalAsync();

                    if (result == "delete")
                    {
                        await DeleteSpecificPhoto(photoIndex);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StockTakePage] Error showing photo: {ex.Message}");
                await DisplayAlert("Error", "Could not display photo", "OK");
            }
        }

        private async Task DeleteSpecificPhoto(int photoIndex)
        {
            try
            {
                if (photoIndex >= 0 && photoIndex < _photoPaths.Count)
                {
                    string photoToDelete = _photoPaths[photoIndex];

                    Console.WriteLine($"[StockTakePage] Deleting photo: {photoToDelete}");

                    // Delete physical file
                    if (File.Exists(photoToDelete))
                    {
                        File.Delete(photoToDelete);
                        Console.WriteLine($"[StockTakePage] Deleted photo file: {photoToDelete}");
                    }

                    // Remove from list
                    _photoPaths.RemoveAt(photoIndex);

                    // Update UI
                    await UpdatePhotoDisplay();

                    await DisplayAlert("Photo Deleted", "Photo has been deleted.", "OK");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StockTakePage] Error deleting photo: {ex.Message}");
                await DisplayAlert("Error", "Could not delete photo", "OK");
            }
        }

        private async void OnDeletePhotoClicked(object sender, EventArgs e)
        {
            try
            {
                if (_photoPaths.Count == 0)
                {
                    await DisplayAlert("No Photos", "No photos to delete.", "OK");
                    return;
                }

                string action;
                if (_photoPaths.Count == 1)
                {
                    // Only one photo - simple confirmation
                    bool confirm = await DisplayAlert("Delete Photo",
                        "Are you sure you want to delete this photo?",
                        "Delete", "Cancel");

                    if (!confirm) return;

                    action = "Delete Current";
                }
                else
                {
                    // Multiple photos - give options
                    action = await DisplayActionSheet(
                        $"Delete Photos ({_photoPaths.Count} total)",
                        "Cancel",
                        null,
                        "Delete Current Photo",
                        "Delete All Photos");
                }

                if (action == "Delete Current" || action == "Delete Current Photo")
                {
                    await DeleteCurrentPhoto();
                }
                else if (action == "Delete All Photos")
                {
                    bool confirm = await DisplayAlert("Delete All Photos",
                        $"Are you sure you want to delete all {_photoPaths.Count} photos?",
                        "Delete All", "Cancel");

                    if (confirm)
                    {
                        await DeleteAllPhotos();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StockTakePage] Error in delete photo: {ex.Message}");
                await DisplayAlert("Error", "Could not delete photo", "OK");
            }
        }



        private async Task DeleteAllPhotos()
        {
            try
            {
                Console.WriteLine("[StockTakePage] Deleting all photos");

                // Delete physical files
                foreach (string photoPath in _photoPaths)
                {
                    try
                    {
                        if (File.Exists(photoPath))
                        {
                            File.Delete(photoPath);
                            Console.WriteLine($"[StockTakePage] Deleted photo: {photoPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[StockTakePage] Error deleting photo {photoPath}: {ex.Message}");
                    }
                }

                // Clear the list
                _photoPaths.Clear();

                // Update UI
                await UpdatePhotoDisplay();

                await DisplayAlert("Photos Deleted", "All photos have been deleted.", "OK");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StockTakePage] Error deleting photos: {ex.Message}");
                await DisplayAlert("Error", "Could not delete all photos", "OK");
            }
        }

        private async Task DeleteCurrentPhoto()
        {
            try
            {
                if (_photoPaths.Count == 0) return;

                // Delete the most recent photo (the one being displayed)
                int lastIndex = _photoPaths.Count - 1;
                string photoToDelete = _photoPaths[lastIndex];

                Console.WriteLine($"[StockTakePage] Deleting current photo: {photoToDelete}");

                // Delete physical file
                if (File.Exists(photoToDelete))
                {
                    File.Delete(photoToDelete);
                    Console.WriteLine($"[StockTakePage] Deleted photo file: {photoToDelete}");
                }

                // Remove from list
                _photoPaths.RemoveAt(lastIndex);

                // Update UI
                await UpdatePhotoDisplay();

                await DisplayAlert("Photo Deleted", "Photo has been deleted.", "OK");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StockTakePage] Error deleting current photo: {ex.Message}");
                await DisplayAlert("Error", "Could not delete photo", "OK");
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

                // SHOW LOADING SPINNER
                FullScreenLoadingOverlay.IsVisible = true;

                if (!ValidateData())
                {
                    // Hide loading overlay first
                    FullScreenLoadingOverlay.IsVisible = false;

                    // Show the alert
                    await DisplayAlert("Incomplete Data",
                        "Please fill in all required fields:\n• Vehicle Registration\n• Branch\n• Department",
                        "OK");

                    // Auto-scroll to the Location Assignment section
                    await ScrollToLocationAssignmentSection();

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
                stockTake.GenerateRefId(DateTime.Now);
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
                                    Console.WriteLine($"[StockTakePage] Uploading {_photoPaths.Count} photos to Salesforce");
                                    await UploadPhotosToSalesforceAsync(salesforceId, _photoPaths);
                                }
                                catch (Exception photoEx)
                                {
                                    Console.WriteLine($"[StockTakePage] Photo upload failed: {photoEx.Message}");
                                    // Don't fail the entire submission for photo upload issues
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
                // HIDE LOADING SPINNER AND RESET BUTTON
                FullScreenLoadingOverlay.IsVisible = false;
                SubmitButton.Text = "Submit Stock Take";
                SubmitButton.IsEnabled = true;
            }
        }

        private async Task ScrollToLocationAssignmentSection()
        {
            try
            {
                Console.WriteLine("[StockTakePage] Scrolling to Location Assignment section");

                // Find the ScrollView and Location Assignment Frame
                var scrollView = MainScrollView;
                var locationFrame = LocationAssignmentFrame;

                if (scrollView != null && locationFrame != null)
                {
                    // Scroll to the Location Assignment frame with animation
                    await scrollView.ScrollToAsync(locationFrame, ScrollToPosition.Start, true);

                    Console.WriteLine("[StockTakePage] Successfully scrolled to Location Assignment section");

                    // Optional: Add a brief highlight effect to draw attention
                    await HighlightLocationSection(locationFrame);
                }
                else
                {
                    Console.WriteLine("[StockTakePage] Could not find ScrollView or LocationFrame for scrolling");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StockTakePage] Error scrolling to location section: {ex.Message}");
            }
        }

        // Optional: Add a visual highlight effect to draw attention
        private async Task HighlightLocationSection(Frame locationFrame)
        {
            try
            {
                Console.WriteLine("[StockTakePage] Highlighting Location Assignment section");

                // Store original border color
                var originalBorderColor = locationFrame.BorderColor;

                // Flash the border to draw attention - make it more prominent
                locationFrame.BorderColor = Colors.Red;
                await Task.Delay(400);

                locationFrame.BorderColor = Colors.Orange;
                await Task.Delay(400);

                locationFrame.BorderColor = Colors.Red;
                await Task.Delay(400);

                // Restore original color
                locationFrame.BorderColor = originalBorderColor;

                Console.WriteLine("[StockTakePage] Location Assignment section highlight complete");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StockTakePage] Error highlighting section: {ex.Message}");
            }
        }

        // ADD THIS METHOD FOR PHOTO UPLOAD
        private async Task UploadPhotosToSalesforceAsync(string salesforceRecordId, List<string> photoPaths)
        {
            try
            {
                if (photoPaths == null || photoPaths.Count == 0) return;

                Console.WriteLine($"[StockTakePage] Uploading {photoPaths.Count} photos to Salesforce record: {salesforceRecordId}");

                for (int i = 0; i < photoPaths.Count; i++)
                {
                    string photoPath = photoPaths[i];
                    if (File.Exists(photoPath))
                    {
                        try
                        {
                            Console.WriteLine($"[StockTakePage] Uploading photo {i + 1}/{photoPaths.Count}: {Path.GetFileName(photoPath)}");

                            byte[] fileBytes = await File.ReadAllBytesAsync(photoPath);
                            string fileName = Path.GetFileName(photoPath);

                            // Add index to filename if multiple photos
                            if (photoPaths.Count > 1)
                            {
                                string extension = Path.GetExtension(fileName);
                                string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                                fileName = $"{nameWithoutExt}_{i + 1}{extension}";
                            }

                            // Upload using SalesforceService
                            string fileId = await _salesforceService.UploadFileAsync(salesforceRecordId, fileName, fileBytes, "image/jpeg");
                            Console.WriteLine($"[StockTakePage] Successfully uploaded photo {i + 1} with ID: {fileId}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[StockTakePage] Error uploading photo {i + 1}: {ex.Message}");
                            // Continue with other photos even if one fails
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[StockTakePage] Photo file not found: {photoPath}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StockTakePage] Error during photo upload: {ex.Message}");
                // Don't throw - photos are optional, main record was saved successfully
                throw; // Re-throw to let caller handle it
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

        private string GetSouthAfricaTimestamp()
        {
            try
            {
                var southAfricaTime = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.Now, "South Africa Standard Time");
                return southAfricaTime.ToString("yyyyMMdd_HHmmss");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StockTakePage] Error getting SA time, using local: {ex.Message}");
                return DateTime.Now.ToString("yyyyMMdd_HHmmss");
            }
        }
    }
}