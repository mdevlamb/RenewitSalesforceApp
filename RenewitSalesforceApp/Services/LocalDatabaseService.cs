using RenewitSalesforceApp.Models;
using SQLite;

namespace RenewitSalesforceApp.Services
{
    public class LocalDatabaseService
    {
        private SQLiteAsyncConnection _database;
        private bool _isInitialized = false;
        private readonly SemaphoreSlim _initializationLock = new SemaphoreSlim(1, 1);

        public LocalDatabaseService(string dbPath)
        {
            Console.WriteLine($"LocalDatabaseService constructor called with path: {dbPath}");
            try
            {
                _database = new SQLiteAsyncConnection(dbPath);
                Console.WriteLine("SQLiteAsyncConnection created successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating SQLiteAsyncConnection: {ex.Message}");
                throw;
            }
        }

        public async Task InitializeAsync()
        {
            if (_isInitialized)
                return;

            await _initializationLock.WaitAsync();
            try
            {
                if (!_isInitialized)
                {
                    Console.WriteLine("Beginning database initialization");

                    await _database.CreateTableAsync<LocalUser>();
                    await _database.CreateTableAsync<StockTakeRecord>();

                    _isInitialized = true;
                    Console.WriteLine("Database initialized successfully");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Database initialization error: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
            finally
            {
                _initializationLock.Release();
            }
        }

        private async Task EnsureInitializedAsync()
        {
            if (!_isInitialized)
            {
                await InitializeAsync();
            }
        }

        #region User Methods
        public async Task<LocalUser> GetUserByPinAsync(string pin)
        {
            await EnsureInitializedAsync();
            return await _database.Table<LocalUser>()
                .Where(u => u.PIN == pin)
                .FirstOrDefaultAsync();
        }

        public async Task<int> SaveUserAsync(LocalUser user)
        {
            await EnsureInitializedAsync();
            return await _database.InsertOrReplaceAsync(user);
        }

        public async Task<List<LocalUser>> GetAllUsersAsync()
        {
            await EnsureInitializedAsync();
            return await _database.Table<LocalUser>().ToListAsync();
        }

        public async Task<int> DeleteUserAsync(string userId)
        {
            await EnsureInitializedAsync();
            return await _database.DeleteAsync<LocalUser>(userId);
        }
        #endregion

        #region Stock Take Methods
        public async Task<List<StockTakeRecord>> GetAllStockTakeRecordsAsync()
        {
            await EnsureInitializedAsync();
            return await _database.Table<StockTakeRecord>()
                .OrderByDescending(s => s.Stock_Take_Date__c)
                .ToListAsync();
        }

        public async Task<List<StockTakeRecord>> GetUnsyncedStockTakeRecordsAsync()
        {
            await EnsureInitializedAsync();
            return await _database.Table<StockTakeRecord>()
                .Where(s => !s.IsSynced)
                // Changed from OrderByDescending to OrderBy
                .OrderBy(s => s.Stock_Take_Date__c)
                .ToListAsync();
        }

        public async Task<int> SaveStockTakeRecordAsync(StockTakeRecord stockTake)
        {
            await EnsureInitializedAsync();
            if (stockTake.LocalId != 0)
                return await _database.UpdateAsync(stockTake);
            else
                return await _database.InsertAsync(stockTake);
        }

        public async Task<bool> MarkStockTakeAsSyncedAsync(int localId, string sfId)
        {
            try
            {
                await EnsureInitializedAsync();

                var record = await _database.Table<StockTakeRecord>()
                    .Where(r => r.LocalId == localId)
                    .FirstOrDefaultAsync();

                if (record != null)
                {
                    Console.WriteLine($"[LocalDatabaseService] Marking stock take as synced: LocalId={localId}, SfId={sfId}");

                    record.Id = sfId;
                    record.IsSynced = true;
                    record.SyncTimestamp = DateTime.Now;

                    await _database.UpdateAsync(record);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LocalDatabaseService] Error marking stock take as synced: {ex.Message}");
                return false;
            }
        }

        public async Task<StockTakeRecord> GetStockTakeRecordByIdAsync(int localId)
        {
            await EnsureInitializedAsync();
            return await _database.Table<StockTakeRecord>()
                .Where(s => s.LocalId == localId)
                .FirstOrDefaultAsync();
        }

        // Add this enhanced cleanup method to your LocalDatabaseService class:

        public async Task CleanupSyncedRecordsAsync(int daysOld = 30)
        {
            await EnsureInitializedAsync();

            var cutoffDate = DateTime.Now.AddDays(-daysOld);

            // Clean up records that were successfully synced to Salesforce more than X days ago
            var oldSyncedRecords = await _database.Table<StockTakeRecord>()
                .Where(s => s.IsSynced &&
                           s.SyncTimestamp.HasValue &&
                           s.SyncTimestamp.Value <= cutoffDate)
                .ToListAsync();

            foreach (var record in oldSyncedRecords)
            {
                Console.WriteLine($"[LocalDB] Cleaning up old synced record: {record.Vehicle_Registration__c} (SF ID: {record.Id})");

                // Clean up associated photos BEFORE deleting the database record
                await CleanupRecordPhotosAsync(record);

                // Delete the database record
                await _database.DeleteAsync(record);
            }

            Console.WriteLine($"[LocalDB] Cleaned up {oldSyncedRecords.Count} old synced records and their photos");
        }

        // Add this new method to handle photo cleanup:
        private async Task CleanupRecordPhotosAsync(StockTakeRecord record)
        {
            try
            {
                if (record == null) return;

                var photosToDelete = new List<string>();

                // Collect photo paths from different sources
                if (!string.IsNullOrEmpty(record.PhotoPath))
                {
                    photosToDelete.Add(record.PhotoPath);
                }

                if (!string.IsNullOrEmpty(record.AllPhotoPaths))
                {
                    var allPaths = record.AllPhotoPaths.Split(';', StringSplitOptions.RemoveEmptyEntries);
                    photosToDelete.AddRange(allPaths);
                }

                // Remove duplicates
                photosToDelete = photosToDelete.Distinct().ToList();

                // Delete each photo file
                foreach (string photoPath in photosToDelete)
                {
                    try
                    {
                        if (File.Exists(photoPath))
                        {
                            File.Delete(photoPath);
                            Console.WriteLine($"[LocalDB] Deleted photo file: {Path.GetFileName(photoPath)}");
                        }
                        else
                        {
                            Console.WriteLine($"[LocalDB] Photo file not found (already deleted?): {Path.GetFileName(photoPath)}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[LocalDB] Error deleting photo {photoPath}: {ex.Message}");
                        // Continue with other photos even if one fails
                    }
                }

                if (photosToDelete.Count > 0)
                {
                    Console.WriteLine($"[LocalDB] Cleaned up {photosToDelete.Count} photo files for record {record.Vehicle_Registration__c}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LocalDB] Error during photo cleanup for record: {ex.Message}");
            }
        }

        // Add this method to clean up orphaned photos (photos without database records):
        public async Task CleanupOrphanedPhotosAsync()
        {
            try
            {
                Console.WriteLine("[LocalDB] Starting orphaned photo cleanup");

                // Get the photos directory
                string localAppData = FileSystem.AppDataDirectory;
                string photosFolder = Path.Combine(localAppData, "StockTakePhotos");

                if (!Directory.Exists(photosFolder))
                {
                    Console.WriteLine("[LocalDB] Photos folder doesn't exist, nothing to clean up");
                    return;
                }

                // Get all photo files
                var allPhotoFiles = Directory.GetFiles(photosFolder, "*.jpg", SearchOption.AllDirectories)
                                           .Concat(Directory.GetFiles(photosFolder, "*.png", SearchOption.AllDirectories))
                                           .ToList();

                if (allPhotoFiles.Count == 0)
                {
                    Console.WriteLine("[LocalDB] No photo files found");
                    return;
                }

                // Get all photo paths referenced in database
                var allRecords = await _database.Table<StockTakeRecord>().ToListAsync();
                var referencedPhotos = new HashSet<string>();

                foreach (var record in allRecords)
                {
                    if (!string.IsNullOrEmpty(record.PhotoPath))
                    {
                        referencedPhotos.Add(record.PhotoPath);
                    }

                    if (!string.IsNullOrEmpty(record.AllPhotoPaths))
                    {
                        var paths = record.AllPhotoPaths.Split(';', StringSplitOptions.RemoveEmptyEntries);
                        foreach (var path in paths)
                        {
                            referencedPhotos.Add(path);
                        }
                    }
                }

                // Find orphaned photos
                var orphanedPhotos = allPhotoFiles.Where(file => !referencedPhotos.Contains(file)).ToList();

                // Delete orphaned photos
                foreach (string orphanedPhoto in orphanedPhotos)
                {
                    try
                    {
                        File.Delete(orphanedPhoto);
                        Console.WriteLine($"[LocalDB] Deleted orphaned photo: {Path.GetFileName(orphanedPhoto)}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[LocalDB] Error deleting orphaned photo {orphanedPhoto}: {ex.Message}");
                    }
                }

                Console.WriteLine($"[LocalDB] Orphaned photo cleanup complete. Removed {orphanedPhotos.Count} orphaned files out of {allPhotoFiles.Count} total files");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LocalDB] Error during orphaned photo cleanup: {ex.Message}");
            }
        }

        // Add this method to get storage statistics:
        public async Task<(int recordCount, int photoCount, long totalPhotoSize)> GetStorageStatsAsync()
        {
            try
            {
                await EnsureInitializedAsync();

                // Count database records
                var recordCount = await _database.Table<StockTakeRecord>().CountAsync();

                // Count and measure photos
                string localAppData = FileSystem.AppDataDirectory;
                string photosFolder = Path.Combine(localAppData, "StockTakePhotos");

                if (!Directory.Exists(photosFolder))
                {
                    return (recordCount, 0, 0);
                }

                var photoFiles = Directory.GetFiles(photosFolder, "*.jpg", SearchOption.AllDirectories)
                                         .Concat(Directory.GetFiles(photosFolder, "*.png", SearchOption.AllDirectories))
                                         .ToArray();

                long totalSize = 0;
                foreach (string file in photoFiles)
                {
                    try
                    {
                        var fileInfo = new FileInfo(file);
                        totalSize += fileInfo.Length;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[LocalDB] Error getting file size for {file}: {ex.Message}");
                    }
                }

                return (recordCount, photoFiles.Length, totalSize);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LocalDB] Error getting storage stats: {ex.Message}");
                return (0, 0, 0);
            }
        }
        #endregion
    }
}