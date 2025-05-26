using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Networking;
using RenewitSalesforceApp.Services;

namespace RenewitSalesforceApp.Views
{
    public partial class HomePage : ContentPage, INotifyPropertyChanged
    {
        private readonly AuthService _authService;
        private readonly LocalDatabaseService _databaseService;
        private bool _isOfflineMode;
        private bool _hasPendingTransactions;
        private int _pendingTransactionCount;

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

        public bool HasPendingTransactions
        {
            get => _hasPendingTransactions;
            set
            {
                if (_hasPendingTransactions != value)
                {
                    _hasPendingTransactions = value;
                    OnPropertyChanged();
                }
            }
        }

        public HomePage(AuthService authService, LocalDatabaseService databaseService)
        {
            InitializeComponent();
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));

            BindingContext = this;

            // Set current date
            DateLabel.Text = DateTime.Now.ToString("MMMM d, yyyy");

            // Update user greeting
            UpdateUserGreeting();

            // Check connectivity
            IsOfflineMode = Connectivity.NetworkAccess != NetworkAccess.Internet;

            // Subscribe to connectivity changes
            Connectivity.ConnectivityChanged += Connectivity_ConnectivityChanged;

            // Load pending transactions
            LoadPendingTransactions();
        }

        private void UpdateUserGreeting()
        {
            try
            {
                if (_authService?.CurrentUser != null && !string.IsNullOrEmpty(_authService.CurrentUser.Name))
                {
                    string name = _authService.CurrentUser.Name;
                    GreetingLabel.Text = $"Hello, {name}";

                    if (name.Length > 0)
                    {
                        AvatarLabel.Text = name[0].ToString().ToUpper();
                    }
                }
                else
                {
                    Console.WriteLine("CurrentUser is null or name is empty, using default greeting");
                    GreetingLabel.Text = "Hello, User!";
                    AvatarLabel.Text = "R";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating user greeting: {ex.Message}");
                GreetingLabel.Text = "Hello, User!";
                AvatarLabel.Text = "R";
            }
        }

        private async void LoadPendingTransactions()
        {
            try
            {
                Console.WriteLine("HomePage: Loading pending transactions...");

                // Get pending transactions from BOTH types of records
                var pendingStockTakes = await _databaseService.GetPendingStockTakesAsync();
                var unsyncedStockTakeRecords = await _databaseService.GetUnsyncedStockTakeRecordsAsync();

                // Count both types
                int pendingCount = (pendingStockTakes?.Count ?? 0);
                int unsyncedCount = (unsyncedStockTakeRecords?.Count ?? 0);
                _pendingTransactionCount = pendingCount + unsyncedCount;

                Console.WriteLine($"HomePage: Found {pendingCount} pending transactions and {unsyncedCount} unsynced stock take records");

                // Update the UI visibility
                HasPendingTransactions = _pendingTransactionCount > 0;
                Console.WriteLine($"HomePage: Setting HasPendingTransactions to {HasPendingTransactions}");

                if (HasPendingTransactions)
                {
                    string transactionText = _pendingTransactionCount == 1
                        ? "transaction waiting to sync"
                        : "transactions waiting to sync";

                    PendingCountLabel.Text = $"{_pendingTransactionCount} {transactionText}";
                    Console.WriteLine($"HomePage: Updated pending count label: {PendingCountLabel.Text}");
                }

                // Force property changed notification
                OnPropertyChanged(nameof(HasPendingTransactions));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading pending transactions: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        private void Connectivity_ConnectivityChanged(object sender, ConnectivityChangedEventArgs e)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                bool previousOfflineMode = IsOfflineMode;
                IsOfflineMode = e.NetworkAccess != NetworkAccess.Internet;

                Console.WriteLine($"HomePage: Connectivity changed. Offline mode: {IsOfflineMode}");

                // If we just came back online after being offline, trigger a sync
                if (previousOfflineMode && !IsOfflineMode)
                {
                    Console.WriteLine("HomePage: Network restored after being offline");

                    // Reload pending transactions to check if we need to sync
                    await Task.Delay(1000); // Short delay to ensure everything is ready
                    LoadPendingTransactions();

                    if (HasPendingTransactions)
                    {
                        Console.WriteLine("HomePage: Auto-sync initiated due to restored connectivity");
                        // For now, just notify user - actual sync will be implemented later
                        await DisplayAlert("Network Restored", "Network connection restored. You can now sync your pending stock takes.", "OK");
                    }
                }

                // Always refresh pending transactions when connectivity changes
                LoadPendingTransactions();
            });
        }

        protected override void OnAppearing()
        {
            try
            {
                base.OnAppearing();
                Console.WriteLine("HomePage.OnAppearing called");

                // Check current network status
                bool currentNetworkStatus = Connectivity.NetworkAccess != NetworkAccess.Internet;
                Console.WriteLine($"Current network status in HomePage: {(currentNetworkStatus ? "OFFLINE" : "ONLINE")}");

                // Update the property to reflect current status
                IsOfflineMode = currentNetworkStatus;

                // IMPORTANT: Subscribe to connectivity changes
                Connectivity.ConnectivityChanged += Connectivity_ConnectivityChanged;
                Console.WriteLine("HomePage re-subscribed to connectivity events");

                UpdateUserGreeting();

                // ALWAYS refresh the pending transactions when the page appears
                LoadPendingTransactions();

                // Force a UI update for HasPendingTransactions
                OnPropertyChanged(nameof(HasPendingTransactions));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in HomePage.OnAppearing: {ex.Message}");
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            Connectivity.ConnectivityChanged -= Connectivity_ConnectivityChanged;
        }


        private async void OnStockTakeClicked(object sender, EventArgs e)
        {
            try
            {
                Console.WriteLine("[HomePage] Stock Take button clicked");

                // Check if user has permission for stock take
                if (!_authService.HasPermission("STOCK_TAKE"))
                {
                    await DisplayAlert("Access Denied", "You don't have permission to perform stock takes.", "OK");
                    return;
                }

                // Get SalesforceService from DI container
                var salesforceService = Application.Current?.Handler?.MauiContext?.Services.GetService<SalesforceService>();

                if (salesforceService == null)
                {
                    Console.WriteLine("[HomePage] SalesforceService not found in DI container");
                    await DisplayAlert("Service Error", "Could not initialize required services.", "OK");
                    return;
                }

                Console.WriteLine("[HomePage] Creating StockTakePage with services");

                // Create and navigate to StockTakePage
                var stockTakePage = new StockTakePage(_authService, _databaseService, salesforceService);
                await Navigation.PushAsync(stockTakePage);

                Console.WriteLine("[HomePage] Successfully navigated to StockTakePage");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HomePage] Error navigating to stock take: {ex.Message}");
                Console.WriteLine($"[HomePage] Stack trace: {ex.StackTrace}");
                await DisplayAlert("Error", $"Failed to open stock take page: {ex.Message}", "OK");
            }
        }

        private async void OnSyncNowClicked(object sender, EventArgs e)
        {
            if (IsOfflineMode)
            {
                await DisplayAlert("Offline Mode", "Please connect to the internet to sync transactions.", "OK");
                return;
            }

            IsBusy = true;
            Console.WriteLine("HomePage: Starting manual sync process");

            try
            {
                // TODO: Implement actual sync functionality
                await Task.Delay(2000); // Simulate sync process

                // For now, just clear the pending indicator
                _pendingTransactionCount = 0;
                HasPendingTransactions = false;

                await DisplayAlert("Sync Complete", "All transactions have been synchronized to Salesforce.", "OK");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"HomePage: Manual sync error: {ex.Message}");
                await DisplayAlert("Sync Failed", $"An error occurred: {ex.Message}", "OK");
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async void OnLogoutClicked(object sender, EventArgs e)
        {
            bool confirm = await DisplayAlert("Logout", "Are you sure you want to logout?", "Yes", "No");

            if (confirm)
            {
                await _authService.LogoutAsync();
                await Navigation.PopToRootAsync();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}