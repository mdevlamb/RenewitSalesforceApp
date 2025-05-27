using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.Networking;
using RenewitSalesforceApp.Services;

namespace RenewitSalesforceApp.Services
{
    public class SyncService
    {
        private readonly StockTakeService _stockTakeService;
        private readonly Timer _cleanupTimer;
        private bool _isOnline = false;
        private readonly SemaphoreSlim _syncSemaphore = new SemaphoreSlim(1, 1);

        public SyncService(StockTakeService stockTakeService)
        {
            _stockTakeService = stockTakeService;

            // Keep cleanup timer - runs once daily to clean old records
            _cleanupTimer = new Timer(PerformCleanup, null, TimeSpan.FromHours(1), TimeSpan.FromHours(24));

            // Enable connectivity change events
            Connectivity.ConnectivityChanged += OnConnectivityChanged;
            _isOnline = Connectivity.NetworkAccess == NetworkAccess.Internet;

            Console.WriteLine($"[SyncService] Initialized. Online: {_isOnline}");
        }

        private void OnConnectivityChanged(object sender, ConnectivityChangedEventArgs e)
        {
            var wasOnline = _isOnline;
            _isOnline = e.NetworkAccess == NetworkAccess.Internet;

            Console.WriteLine($"[SyncService] Connectivity changed. Was: {wasOnline}, Now: {_isOnline}");

            if (!wasOnline && _isOnline)
            {
                Console.WriteLine("[SyncService] Connection restored, triggering sync");
                _ = Task.Run(async () => await TriggerSyncAsync());
            }
        }

        public async Task TriggerSyncAsync()
        {
            // Prevent multiple simultaneous syncs
            if (!await _syncSemaphore.WaitAsync(1000))
            {
                Console.WriteLine("[SyncService] Sync already in progress, skipping");
                return;
            }

            try
            {
                if (!_isOnline)
                {
                    Console.WriteLine("[SyncService] Not online, skipping sync");
                    return;
                }

                var pendingRecords = await _stockTakeService.GetUnsyncedStockTakesAsync();
                if (pendingRecords.Count > 0)
                {
                    Console.WriteLine($"[SyncService] Found {pendingRecords.Count} records to sync");
                    var syncedCount = await _stockTakeService.SyncStockTakesAsync();
                    Console.WriteLine($"[SyncService] Synced {syncedCount} records successfully");
                }
                else
                {
                    Console.WriteLine("[SyncService] No pending records to sync");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SyncService] Sync error: {ex.Message}");
            }
            finally
            {
                _syncSemaphore.Release();
            }
        }

        private async void PerformCleanup(object state)
        {
            try
            {
                Console.WriteLine("[SyncService] Starting daily cleanup of old synced records");
                var dbService = Application.Current?.Handler?.MauiContext?.Services.GetService<LocalDatabaseService>();
                if (dbService != null)
                {
                    await dbService.CleanupSyncedRecordsAsync(30);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SyncService] Cleanup error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
            Connectivity.ConnectivityChanged -= OnConnectivityChanged;
            _syncSemaphore?.Dispose();
        }
    }
}