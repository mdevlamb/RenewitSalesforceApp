using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
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

                // Get unsynced stock take records (only source of pending data now)
                var unsyncedStockTakeRecords = await _databaseService.GetUnsyncedStockTakeRecordsAsync();

                _pendingTransactionCount = unsyncedStockTakeRecords?.Count ?? 0;

                Console.WriteLine($"HomePage: Found {_pendingTransactionCount} unsynced stock take records");

                // Update the UI visibility
                HasPendingTransactions = _pendingTransactionCount > 0;

                Console.WriteLine($"HomePage: Setting HasPendingTransactions to {HasPendingTransactions}");

                if (HasPendingTransactions)
                {
                    string transactionText = _pendingTransactionCount == 1
                        ? "stock take waiting to sync"
                        : "stock takes waiting to sync";
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

                    // Short delay to let SyncService start first (it responds to same connectivity event)
                    await Task.Delay(2000);

                    // Reload to check current status
                    LoadPendingTransactions();

                    // Only try to sync if SyncService hasn't already handled it
                    if (HasPendingTransactions)
                    {
                        Console.WriteLine("HomePage: Auto-sync initiated due to restored connectivity");
                        await PerformAutoSyncWithUIFeedback("Network connection restored.");
                    }
                    else
                    {
                        Console.WriteLine("HomePage: No pending transactions found (likely synced by background service)");
                    }
                }

                // Always refresh pending transactions when connectivity changes
                LoadPendingTransactions();
            });
        }

        protected override async void OnAppearing()
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

                // NO AUTO-SYNC - Let user manually sync if needed
                Console.WriteLine("HomePage: Page loaded, pending transactions refreshed. User can manually sync if needed.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in HomePage.OnAppearing: {ex.Message}");
            }
        }

        private async Task PerformAutoSyncWithUIFeedback(string reason)
        {
            try
            {
                Console.WriteLine($"HomePage: Starting auto-sync process. Reason: {reason}");

                // Show loading indicator
                IsBusy = true;

                // Get StockTakeService from DI
                var stockTakeService = Application.Current?.Handler?.MauiContext?.Services.GetService<StockTakeService>();
                if (stockTakeService == null)
                {
                    Console.WriteLine("HomePage: StockTakeService not available for auto-sync");
                    return;
                }

                // Perform the sync
                int syncedCount = await stockTakeService.SyncStockTakesAsync();

                Console.WriteLine($"HomePage: Auto-sync completed. Synced count: {syncedCount}");

                // Small delay to ensure sync is fully complete
                await Task.Delay(500);

                // Update the pending count after sync
                LoadPendingTransactions();

                // Show appropriate message to user - ensure we're on UI thread and add delay
                await Task.Delay(1000); // Give UI time to settle after hiding loading spinner

                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    Console.WriteLine($"HomePage: About to show alert. SyncedCount: {syncedCount}, HasPendingTransactions: {HasPendingTransactions}");

                    if (syncedCount > 0)
                    {
                        Console.WriteLine($"HomePage: Showing success alert for {syncedCount} synced records");
                        await DisplayAlert("Auto-Sync Complete",
                            $"{reason}\n\nSuccessfully synced {syncedCount} stock take{(syncedCount > 1 ? "s" : "")} to Salesforce!",
                            "Great!");
                        Console.WriteLine($"HomePage: Success alert displayed");
                    }
                    else
                    {
                        Console.WriteLine("HomePage: Auto-sync completed with 0 records synced");

                        // Check if sync was skipped due to another sync in progress
                        if (HasPendingTransactions)
                        {
                            // There are still pending items, so sync was likely skipped
                            await DisplayAlert("Auto-Sync In Progress",
                                $"{reason}\n\nSync is already running in the background. Please wait for it to complete.",
                                "OK");
                            Console.WriteLine($"HomePage: Sync in progress alert displayed");
                        }
                        else
                        {
                            // No pending items, so sync was successful (done by background service)
                            await DisplayAlert("Auto-Sync Complete",
                                $"{reason}\n\nSync completed successfully by background service.",
                                "OK");
                            Console.WriteLine($"HomePage: Background sync completion alert displayed");
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"HomePage: Auto-sync error: {ex.Message}");

                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await DisplayAlert("Auto-Sync Failed",
                        $"{reason} Failed to sync stock takes. Will retry later.\n\nError: {ex.Message}",
                        "OK");
                });
            }
            finally
            {
                // Hide loading indicator with delay to ensure user sees the completion
                await Task.Delay(500);
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    Console.WriteLine("HomePage: Hiding loading indicator");
                    IsBusy = false;
                });
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

            // Get StockTakeService from DI
            var stockTakeService = Application.Current?.Handler?.MauiContext?.Services.GetService<StockTakeService>();
            if (stockTakeService == null)
            {
                await DisplayAlert("Service Error", "Sync service not available.", "OK");
                return;
            }

            // Show user we're working
            Button syncButton = sender as Button;
            if (syncButton != null)
            {
                syncButton.IsEnabled = false;
                syncButton.Text = "Syncing...";
            }

            // Show loading indicator
            IsBusy = true;

            try
            {
                Console.WriteLine("HomePage: Starting manual sync process");

                // Perform the sync
                int syncedCount = await stockTakeService.SyncStockTakesAsync();

                Console.WriteLine($"HomePage: Sync completed, synced count: {syncedCount}");

                // Important: Wait a moment before refreshing to ensure sync is fully complete
                await Task.Delay(500);

                // Refresh the pending transactions count on the UI thread
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    LoadPendingTransactions();
                });

                // Show result message on UI thread
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    if (syncedCount > 0)
                    {
                        await DisplayAlert("Sync Complete",
                            $"Successfully synced {syncedCount} stock take{(syncedCount != 1 ? "s" : "")} to Salesforce.",
                            "OK");
                    }
                    else
                    {
                        await DisplayAlert("Sync Complete",
                            "No records needed syncing or all sync attempts failed.",
                            "OK");
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"HomePage: Manual sync error: {ex.Message}");

                // Show error on UI thread
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await DisplayAlert("Sync Failed", $"An error occurred: {ex.Message}", "OK");
                });
            }
            finally
            {
                // Reset UI on main thread
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    IsBusy = false;
                    if (syncButton != null)
                    {
                        syncButton.IsEnabled = true;
                        syncButton.Text = "Sync";
                    }
                });
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