using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Authentication;
using RenewitSalesforceApp.Services;

namespace RenewitSalesforceApp.Views
{
    public partial class PinLoginPage : ContentPage, INotifyPropertyChanged
    {
        private AuthService _authService;
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

        // Parameterless constructor
        public PinLoginPage()
        {
            try
            {
                Console.WriteLine("RenewitPinLoginPage parameterless constructor called");
                InitializeComponent();

                // Check network status
                IsOfflineMode = Connectivity.NetworkAccess != NetworkAccess.Internet;

                // Subscribe to connectivity changes
                Connectivity.ConnectivityChanged += Connectivity_ConnectivityChanged;

                BindingContext = this;

                Console.WriteLine("RenewitPinLoginPage initialized without AuthService");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in RenewitPinLoginPage constructor: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        // Parameterized constructor
        public PinLoginPage(AuthService authService)
        {
            try
            {
                Console.WriteLine("RenewitPinLoginPage parameterized constructor called");
                InitializeComponent();

                _authService = authService ??
                    throw new ArgumentNullException(nameof(authService), "AuthService cannot be null");

                Console.WriteLine("AuthService injected successfully");

                // Check network status
                IsOfflineMode = Connectivity.NetworkAccess != NetworkAccess.Internet;

                // Subscribe to connectivity changes
                Connectivity.ConnectivityChanged += Connectivity_ConnectivityChanged;

                BindingContext = this;

                Console.WriteLine("RenewitPinLoginPage fully initialized with AuthService");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in RenewitPinLoginPage constructor with AuthService: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");

                // Continue with initialization even if AuthService is null
                InitializeComponent();
                IsOfflineMode = Connectivity.NetworkAccess != NetworkAccess.Internet;
                Connectivity.ConnectivityChanged += Connectivity_ConnectivityChanged;
                BindingContext = this;
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
                // Unsubscribe from events
                Connectivity.ConnectivityChanged -= Connectivity_ConnectivityChanged;
                Console.WriteLine("RenewitPinLoginPage.OnDisappearing called");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in OnDisappearing: {ex.Message}");
            }
        }

        private async void OnLoginButtonClicked(object sender, EventArgs e)
        {
            Console.WriteLine("Renewit login button clicked");
            if (string.IsNullOrEmpty(PinEntry.Text))
            {
                await DisplayAlert("Error", "Please enter your PIN", "OK");
                return;
            }

            // Check if authService is available
            if (_authService == null)
            {
                Console.WriteLine("AuthService is null, trying to resolve...");
                // Try to get it from services
                try
                {
                    _authService = Application.Current?.Handler?.MauiContext?.Services.GetService<AuthService>();
                    Console.WriteLine($"AuthService resolved: {_authService != null}");
                    // If still null, show error
                    if (_authService == null)
                    {
                        await DisplayAlert("Error", "Authentication service is not available. Please restart the app.", "OK");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error resolving AuthService: {ex.Message}");
                    await DisplayAlert("Service Error", $"Failed to initialize authentication: {ex.Message}", "OK");
                    return;
                }
            }

            // Show loading overlay
            LoadingOverlay.IsVisible = true;
            LoadingIndicator.IsRunning = true;
            LoginButton.IsEnabled = false;

            try
            {
                Console.WriteLine($"Attempting to authenticate with PIN: {PinEntry.Text.Length} digits");

                // Authenticate with PIN
                var authResult = await _authService.AuthenticateAsync(PinEntry.Text);
                bool authenticated = authResult.Success;
                string errorMessage = authResult.ErrorMessage;

                Console.WriteLine($"Authentication result: {authenticated}");

                if (authenticated)
                {
                    // Clear PIN entry
                    PinEntry.Text = string.Empty;
                    Console.WriteLine("Authentication successful, navigating to HomePage");

                    // Navigate to home page after successful login
                    var homePage = new HomePage(
                        _authService,
                        Application.Current?.Handler?.MauiContext?.Services.GetService<LocalDatabaseService>()
                    );
                    await Navigation.PushAsync(homePage);
                }
                else
                {
                    Console.WriteLine($"Authentication failed: {errorMessage}");
                    // Display the specific error message from the auth service
                    await DisplayAlert("Login Failed", errorMessage ?? "Invalid PIN. Please try again.", "OK");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Authentication error: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                await DisplayAlert("Login Error", $"An error occurred: {ex.Message}", "OK");
            }
            finally
            {
                // Hide loading overlay
                LoadingOverlay.IsVisible = false;
                LoadingIndicator.IsRunning = false;
                LoginButton.IsEnabled = true;
            }
        }

        protected override void OnAppearing()
        {
            try
            {
                base.OnAppearing();
                Console.WriteLine("RenewitPinLoginPage.OnAppearing called");

                // If _authService is null, try to get it from the DI container
                if (_authService == null)
                {
                    var services = Application.Current?.Handler?.MauiContext?.Services;
                    if (services != null)
                    {
                        _authService = services.GetService<AuthService>();
                        Console.WriteLine($"AuthService resolved in OnAppearing: {_authService != null}");
                    }
                    else
                    {
                        Console.WriteLine("Services not available in OnAppearing");
                    }
                }

                // IMPORTANT: Unsubscribe first to prevent double subscription
                Connectivity.ConnectivityChanged -= Connectivity_ConnectivityChanged;

                // Check network status and update UI
                IsOfflineMode = Connectivity.NetworkAccess != NetworkAccess.Internet;
                Console.WriteLine($"RenewitPinLoginPage network status: {(IsOfflineMode ? "OFFLINE" : "ONLINE")}");

                // Subscribe to connectivity changes
                Connectivity.ConnectivityChanged += Connectivity_ConnectivityChanged;
                Console.WriteLine("RenewitPinLoginPage subscribed to connectivity events");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in OnAppearing: {ex.Message}");
            }
        }

        // INotifyPropertyChanged implementation
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}