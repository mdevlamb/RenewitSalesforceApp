using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using SQLite;
using RenewitSalesforceApp.Models;

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

                    // Create tables for Renewit app - just users and stock takes
                    await _database.CreateTableAsync<LocalUser>();
                    await _database.CreateTableAsync<StockTakeRecord>();
                    await _database.CreateTableAsync<PendingStockTake>();

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
                .OrderByDescending(s => s.Stock_Take_Date)
                .ToListAsync();
        }

        public async Task<List<StockTakeRecord>> GetUnsyncedStockTakeRecordsAsync()
        {
            await EnsureInitializedAsync();
            return await _database.Table<StockTakeRecord>()
                .Where(s => !s.IsSynced)
                .OrderByDescending(s => s.Stock_Take_Date)
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
        #endregion

        #region Pending Stock Take Methods
        public async Task<List<PendingStockTake>> GetPendingStockTakesAsync()
        {
            await EnsureInitializedAsync();
            return await _database.Table<PendingStockTake>()
                .Where(s => !s.IsSynced)
                .ToListAsync();
        }

        public async Task<int> SavePendingStockTakeAsync(PendingStockTake stockTake)
        {
            await EnsureInitializedAsync();
            return await _database.InsertAsync(stockTake);
        }

        public async Task<int> UpdatePendingStockTakeAsync(PendingStockTake stockTake)
        {
            await EnsureInitializedAsync();
            return await _database.UpdateAsync(stockTake);
        }

        public async Task<int> DeletePendingStockTakeAsync(int id)
        {
            await EnsureInitializedAsync();
            return await _database.DeleteAsync<PendingStockTake>(id);
        }
        #endregion
    }
}