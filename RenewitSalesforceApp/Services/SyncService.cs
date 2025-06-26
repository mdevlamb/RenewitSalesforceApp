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

            // Keep cleanup timer - runs once daily to clean old records AND photos
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

        // ENHANCED: Now cleans up both database records AND photos
        private async void PerformCleanup(object state)
        {
            try
            {
                Console.WriteLine("[SyncService] Starting daily cleanup of old synced records and photos");

                var dbService = Application.Current?.Handler?.MauiContext?.Services.GetService<LocalDatabaseService>();
                if (dbService != null)
                {
                    // Get storage stats before cleanup
                    var (recordsBefore, photosBefore, sizeBefore) = await dbService.GetStorageStatsAsync();
                    Console.WriteLine($"[SyncService] Before cleanup: {recordsBefore} records, {photosBefore} photos, {sizeBefore / 1024 / 1024:F1} MB");

                    // Clean up old synced records (30 days) AND their photos
                    await dbService.CleanupSyncedRecordsAsync(30);

                    // Clean up any orphaned photos (photos without database records)
                    await dbService.CleanupOrphanedPhotosAsync();

                    // Get storage stats after cleanup
                    var (recordsAfter, photosAfter, sizeAfter) = await dbService.GetStorageStatsAsync();
                    Console.WriteLine($"[SyncService] After cleanup: {recordsAfter} records, {photosAfter} photos, {sizeAfter / 1024 / 1024:F1} MB");

                    var recordsRemoved = recordsBefore - recordsAfter;
                    var photosRemoved = photosBefore - photosAfter;
                    var spaceFreed = (sizeBefore - sizeAfter) / 1024 / 1024;

                    Console.WriteLine($"[SyncService] Cleanup complete: Removed {recordsRemoved} records, {photosRemoved} photos, freed {spaceFreed:F1} MB");
                }
                else
                {
                    Console.WriteLine("[SyncService] Could not get LocalDatabaseService for cleanup");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SyncService] Cleanup error: {ex.Message}");
            }
        }

        // ADD: Manual cleanup method that can be called from UI
        public async Task PerformManualCleanupAsync()
        {
            try
            {
                Console.WriteLine("[SyncService] Starting manual cleanup");

                var dbService = Application.Current?.Handler?.MauiContext?.Services.GetService<LocalDatabaseService>();
                if (dbService != null)
                {
                    // Clean up old synced records AND their photos
                    await dbService.CleanupSyncedRecordsAsync(30);

                    // Clean up orphaned photos
                    await dbService.CleanupOrphanedPhotosAsync();

                    Console.WriteLine("[SyncService] Manual cleanup complete");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SyncService] Manual cleanup error: {ex.Message}");
                throw; // Re-throw so UI can handle it
            }
        }

        // ADD: Get storage information for UI display
        public async Task<(int records, int photos, long sizeBytes)> GetStorageInfoAsync()
        {
            try
            {
                var dbService = Application.Current?.Handler?.MauiContext?.Services.GetService<LocalDatabaseService>();
                if (dbService != null)
                {
                    return await dbService.GetStorageStatsAsync();
                }
                return (0, 0, 0);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SyncService] Error getting storage info: {ex.Message}");
                return (0, 0, 0);
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