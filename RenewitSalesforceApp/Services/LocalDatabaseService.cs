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
                .OrderByDescending(s => s.Stock_Take_Date__c)
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
                await _database.DeleteAsync(record);
            }

            Console.WriteLine($"[LocalDB] Cleaned up {oldSyncedRecords.Count} old synced records");
        }
        #endregion
    }
}