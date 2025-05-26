using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using RenewitSalesforceApp.Models;
using Microsoft.Maui.Devices.Sensors;

namespace RenewitSalesforceApp.Services
{
    public class StockTakeService
    {
        private readonly LocalDatabaseService _dbService;
        private readonly SalesforceService _sfService;
        private readonly AuthService _authService;

        private readonly SemaphoreSlim _syncSemaphore = new SemaphoreSlim(1, 1);
        private readonly HashSet<int> _syncingRecords = new HashSet<int>();

        public StockTakeService(
            LocalDatabaseService dbService,
            SalesforceService sfService,
            AuthService authService)
        {
            _dbService = dbService;
            _sfService = sfService;
            _authService = authService;
        }

        public async Task<StockTakeRecord> CreateStockTakeAsync(
            string vehicleRegistration,
            string discRegData,
            string yards,
            string yardLocation,
            List<string> photoPaths = null,
            string comments = null,
            double? latitude = null,
            double? longitude = null,

            // Additional vehicle details from barcode scan
            string licenseNumber = null,
            string make = null,
            string model = null,
            string colour = null,
            string vehicleType = null,
            string vin = null,
            string engineNumber = null,
            string licenseExpiryDate = null,
            string notes = null)
        {
            try
            {
                Console.WriteLine($"[StockTakeService] Creating stock take for vehicle: {vehicleRegistration}");

                var stockTake = new StockTakeRecord
                {
                    // Main identification
                    DISC_REG__c = discRegData,
                    Vehicle_Registration__c = vehicleRegistration,
                    License_Number__c = licenseNumber,

                    // Vehicle details from barcode scan
                    Make__c = make,
                    Model__c = model,
                    Colour__c = colour,
                    Vehicle_Type__c = vehicleType,
                    VIN__c = vin,
                    Engine_Number__c = engineNumber,
                    License_Expiry_Date__c = licenseExpiryDate,

                    // Location
                    Yards__c = yards,
                    Yard_Location__c = yardLocation,

                    // Comments
                    Comments__c = comments,
                    Notes__c = notes,

                    // Sync tracking
                    IsSynced = false,
                    SyncAttempts = 0
                };

                // Generate reference ID with timestamp
                stockTake.GenerateRefId();

                // Set stock take date and user using helper methods
                stockTake.SetStockTakeDate(DateTime.Now);
                stockTake.SetStockTakeBy(_authService?.CurrentUser?.Name ?? "Unknown User");

                // Set GPS coordinates using helper method
                stockTake.SetGPSCoordinates(latitude, longitude);

                // Handle photo paths using new field names
                if (photoPaths != null && photoPaths.Count > 0)
                {
                    stockTake.HasPhoto = true;
                    stockTake.PhotoCount = photoPaths.Count;
                    stockTake.PhotoPath = photoPaths[0]; // Primary photo
                    stockTake.AllPhotoPaths = string.Join(";", photoPaths);

                    Console.WriteLine($"[StockTakeService] Added {photoPaths.Count} photos to stock take");
                }
                else
                {
                    stockTake.HasPhoto = false;
                    stockTake.PhotoCount = 0;
                }

                // Save to local database
                await _dbService.SaveStockTakeRecordAsync(stockTake);
                Console.WriteLine($"[StockTakeService] Stock take saved locally with ID: {stockTake.LocalId}");

                // Mark that we have pending stock takes
                Preferences.Set("HasPendingStockTakes", true);

                return stockTake;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StockTakeService] Error creating stock take: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Sync all pending stock takes to Salesforce - Simplified without background sync competition
        /// </summary>
        public async Task<int> SyncStockTakesAsync()
        {
            // Prevent concurrent sync operations with reasonable wait time
            if (!await _syncSemaphore.WaitAsync(5000)) // 5 second wait
            {
                Console.WriteLine("[StockTakeService] Another sync is in progress, please wait");
                return -1; // Return -1 to indicate sync was busy
            }

            try
            {
                if (Connectivity.NetworkAccess != NetworkAccess.Internet)
                {
                    Console.WriteLine("[StockTakeService] No internet connection for sync");
                    return 0;
                }

                Console.WriteLine("[StockTakeService] Starting sync of stock takes");

                int syncedCount = 0;
                var pendingRecords = await _dbService.GetUnsyncedStockTakeRecordsAsync();

                if (pendingRecords.Count == 0)
                {
                    Console.WriteLine("[StockTakeService] No pending records to sync");
                    return 0;
                }

                Console.WriteLine($"[StockTakeService] Found {pendingRecords.Count} records to sync");

                // Ensure authenticated with Salesforce
                await _sfService.EnsureAuthenticatedAsync();

                foreach (var stockTake in pendingRecords)
                {
                    // Skip if already being synced
                    if (_syncingRecords.Contains(stockTake.LocalId))
                    {
                        Console.WriteLine($"[StockTakeService] Record {stockTake.LocalId} already being synced, skipping");
                        continue;
                    }

                    // Mark as being synced
                    _syncingRecords.Add(stockTake.LocalId);

                    try
                    {
                        // Double-check it's still unsynced (could have been synced by another process)
                        var currentRecord = await _dbService.GetStockTakeRecordByIdAsync(stockTake.LocalId);
                        if (currentRecord == null || currentRecord.IsSynced)
                        {
                            Console.WriteLine($"[StockTakeService] Record {stockTake.LocalId} already synced or deleted, skipping");
                            continue;
                        }

                        Console.WriteLine($"[StockTakeService] Syncing record: {stockTake.Vehicle_Registration__c}");

                        // Increment sync attempt count
                        stockTake.SyncAttempts++;
                        await _dbService.SaveStockTakeRecordAsync(stockTake);

                        // Create the record in Salesforce
                        string sfId = await _sfService.CreateStockTakeRecord(stockTake);

                        if (!string.IsNullOrEmpty(sfId))
                        {
                            Console.WriteLine($"[StockTakeService] Successfully created record in Salesforce with ID: {sfId}");

                            // Upload photos if we have them
                            if (!string.IsNullOrEmpty(stockTake.AllPhotoPaths))
                            {
                                await UploadPhotosToSalesforce(sfId, stockTake.AllPhotoPaths);
                            }

                            // Mark as synced in the local database
                            await _dbService.MarkStockTakeAsSyncedAsync(stockTake.LocalId, sfId);
                            Console.WriteLine($"[StockTakeService] Marked record as synced in local database");

                            syncedCount++;
                        }
                        else
                        {
                            Console.WriteLine("[StockTakeService] Failed to create record in Salesforce - empty ID returned");
                            stockTake.SyncErrorMessage = "Failed to create record in Salesforce";
                            await _dbService.SaveStockTakeRecordAsync(stockTake);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[StockTakeService] Error syncing stock take: {ex.Message}");
                        stockTake.SyncErrorMessage = ex.Message;
                        await _dbService.SaveStockTakeRecordAsync(stockTake);
                    }
                    finally
                    {
                        // Remove from syncing set
                        _syncingRecords.Remove(stockTake.LocalId);
                    }
                }

                Console.WriteLine($"[StockTakeService] Sync completed. Successfully synced {syncedCount} records");

                // Check if we still have pending records
                var remainingRecords = await _dbService.GetUnsyncedStockTakeRecordsAsync();
                bool stillHasPending = remainingRecords != null && remainingRecords.Count > 0;
                Preferences.Set("HasPendingStockTakes", stillHasPending);

                return syncedCount;
            }
            finally
            {
                _syncSemaphore.Release();
            }
        }

        private async Task UploadPhotosToSalesforce(string salesforceRecordId, string allPhotoPaths)
        {
            try
            {
                if (string.IsNullOrEmpty(allPhotoPaths)) return;

                string[] photoPathArray = allPhotoPaths.Split(';');
                Console.WriteLine($"[StockTakeService] Uploading {photoPathArray.Length} photos to Salesforce");

                for (int i = 0; i < photoPathArray.Length; i++)
                {
                    string photoPath = photoPathArray[i];
                    if (File.Exists(photoPath))
                    {
                        try
                        {
                            Console.WriteLine($"[StockTakeService] Uploading photo {i + 1}/{photoPathArray.Length}: {Path.GetFileName(photoPath)}");

                            byte[] fileBytes = await File.ReadAllBytesAsync(photoPath);
                            string fileName = Path.GetFileName(photoPath);

                            // Add index to filename if multiple photos
                            if (photoPathArray.Length > 1)
                            {
                                string extension = Path.GetExtension(fileName);
                                string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                                fileName = $"{nameWithoutExt}_{i + 1}{extension}";
                            }

                            // Upload using your existing method
                            string fileId = await _sfService.UploadFileAsync(salesforceRecordId, fileName, fileBytes, "image/jpeg");
                            Console.WriteLine($"[StockTakeService] Successfully uploaded photo {i + 1} with ID: {fileId}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[StockTakeService] Error uploading photo {i + 1}: {ex.Message}");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[StockTakeService] Photo file not found: {photoPath}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StockTakeService] Error during photo upload: {ex.Message}");
            }
        }

        public async Task<List<StockTakeRecord>> GetUnsyncedStockTakesAsync()
        {
            return await _dbService.GetUnsyncedStockTakeRecordsAsync();
        }
    }
}