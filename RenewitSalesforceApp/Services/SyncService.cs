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
        private bool _isOnline = false;

        public SyncService(StockTakeService stockTakeService)
        {
            _stockTakeService = stockTakeService;

            // Check connectivity every 30 seconds
            _syncTimer = new Timer(CheckAndSync, null, TimeSpan.Zero, TimeSpan.FromSeconds(30));

            // Subscribe to connectivity changes
            Connectivity.ConnectivityChanged += OnConnectivityChanged;
            _isOnline = Connectivity.NetworkAccess == NetworkAccess.Internet;
        }

        private void OnConnectivityChanged(object sender, ConnectivityChangedEventArgs e)
        {
            var wasOnline = _isOnline;
            _isOnline = e.NetworkAccess == NetworkAccess.Internet;

            if (!wasOnline && _isOnline)
            {
                Console.WriteLine("[SyncService] Connection restored, triggering sync");
                _ = Task.Run(async () => await TriggerSyncAsync());
            }
        }

        private async void CheckAndSync(object state)
        {
            if (_isOnline)
            {
                await TriggerSyncAsync();
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
    }
}