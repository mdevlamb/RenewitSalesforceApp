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
        private readonly Timer _syncTimer;
        private readonly Timer _cleanupTimer;
        private bool _isOnline = false;

        public SyncService(StockTakeService stockTakeService)
        {
            _stockTakeService = stockTakeService;

            // REDUCED: Check connectivity every 2 minutes instead of 30 seconds
            _syncTimer = new Timer(CheckAndSync, null, TimeSpan.Zero, TimeSpan.FromMinutes(2));

            _cleanupTimer = new Timer(PerformCleanup, null, TimeSpan.FromHours(1), TimeSpan.FromHours(24));

            // Subscribe to connectivity changes
            // Connectivity.ConnectivityChanged += OnConnectivityChanged;
            _isOnline = Connectivity.NetworkAccess == NetworkAccess.Internet;
        }

        //private void OnConnectivityChanged(object sender, ConnectivityChangedEventArgs e)
        //{
        //    var wasOnline = _isOnline;
        //    _isOnline = e.NetworkAccess == NetworkAccess.Internet;

        //    if (!wasOnline && _isOnline)
        //    {
        //        Console.WriteLine("[SyncService] Connection restored, triggering sync");
        //        _ = Task.Run(async () => await TriggerSyncAsync());
        //    }
        //}

        private async void CheckAndSync(object state)
        {
            if (_isOnline)
            {
                var pendingRecords = await _stockTakeService.GetUnsyncedStockTakesAsync();
                if (pendingRecords?.Count > 0)
                {
                    await TriggerSyncAsync();
                }
            }
        }

        public async Task TriggerSyncAsync()
        {
            try
            {
                if (!_isOnline) return;

                var pendingRecords = await _stockTakeService.GetUnsyncedStockTakesAsync();
                if (pendingRecords.Count > 0)
                {
                    Console.WriteLine($"[SyncService] Found {pendingRecords.Count} records to sync");
                    var syncedCount = await _stockTakeService.SyncStockTakesAsync();
                    Console.WriteLine($"[SyncService] Synced {syncedCount} records");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SyncService] Sync error: {ex.Message}");
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
    }
}