using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RenewitSalesforceApp.Models;

namespace RenewitSalesforceApp.Services
{
    public class AuthService
    {
        private readonly LocalDatabaseService _dbService;
        private readonly SalesforceService _salesforceService;
        private LocalUser _currentUser;

        public LocalUser CurrentUser => _currentUser;

        public AuthService(LocalDatabaseService dbService, SalesforceService salesforceService)
        {
            _dbService = dbService ?? throw new ArgumentNullException(nameof(dbService));
            _salesforceService = salesforceService ?? throw new ArgumentNullException(nameof(salesforceService));
        }

        /// <summary>
        /// Authenticates user with PIN and returns success status with error message
        /// </summary>
        public async Task<(bool Success, string ErrorMessage)> AuthenticateAsync(string pin)
        {
            try
            {
                Console.WriteLine($"[AuthService] Attempting authentication with PIN length: {pin?.Length ?? 0}");

                if (string.IsNullOrEmpty(pin))
                {
                    return (false, "Please enter your PIN");
                }

                // Step 1: Check if we're online and can authenticate against Salesforce
                bool isOnline = Connectivity.NetworkAccess == NetworkAccess.Internet;
                Console.WriteLine($"[AuthService] Network status: {(isOnline ? "ONLINE" : "OFFLINE")}");

                LocalUser user = null;

                if (isOnline)
                {
                    Console.WriteLine("[AuthService] Online - attempting to authenticate against Salesforce");
                    try
                    {
                        // Authenticate against Salesforce and sync ONLY this user
                        user = await AuthenticateAndSyncUserAsync(pin);

                        if (user != null)
                        {
                            Console.WriteLine($"[AuthService] User {user.Name} authenticated and synced from Salesforce");
                        }
                        else
                        {
                            Console.WriteLine("[AuthService] No user found in Salesforce with provided PIN");
                        }
                    }
                    catch (Exception syncEx)
                    {
                        Console.WriteLine($"[AuthService] Salesforce authentication failed: {syncEx.Message}");
                        // If Salesforce fails, fall back to local authentication
                        Console.WriteLine("[AuthService] Falling back to local authentication");
                        user = await _dbService.GetUserByPinAsync(pin);
                    }
                }
                else
                {
                    Console.WriteLine("[AuthService] Offline - using local user database only");
                    user = await _dbService.GetUserByPinAsync(pin);
                }

                // Step 2: Validate user
                if (user == null)
                {
                    Console.WriteLine("[AuthService] No user found with provided PIN");
                    if (isOnline)
                    {
                        return (false, "Invalid PIN. Please ensure you have the correct PIN or contact your administrator.");
                    }
                    else
                    {
                        return (false, "Invalid PIN. Please connect to the internet to authenticate or try again.");
                    }
                }

                if (!user.IsActive)
                {
                    Console.WriteLine($"[AuthService] User {user.Name} is not active");
                    return (false, "Your account is inactive. Please contact administrator.");
                }

                // Step 3: Authentication successful
                _currentUser = user;
                Console.WriteLine($"[AuthService] Authentication successful for user: {user.Name}");

                // Step 4: Update last login info if online
                if (isOnline)
                {
                    _ = Task.Run(async () => await UpdateLastLoginAsync(user));
                }

                return (true, null);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AuthService] Authentication error: {ex.Message}");
                return (false, "Authentication failed. Please try again.");
            }
        }

        /// <summary>
        /// Authenticates a specific PIN against Salesforce and syncs only that user
        /// </summary>
        private async Task<LocalUser> AuthenticateAndSyncUserAsync(string pin)
        {
            try
            {
                Console.WriteLine($"[AuthService] Authenticating PIN against Salesforce: {pin}");

                // Ensure Salesforce connection
                await _salesforceService.EnsureAuthenticatedAsync();

                // Query for the specific PIN only
                string soql = @"SELECT Id, Name, PIN__c, IsActive__c, Permissions__c, LastModifiedDate 
                               FROM Contact 
                               WHERE PIN__c = '" + pin.Replace("'", "\\'") + @"' 
                               AND PIN__c != null 
                               AND PIN__c != '' 
                               LIMIT 1";

                Console.WriteLine($"[AuthService] Executing SOQL for specific PIN");

                var result = await _salesforceService.ExecuteQueryAsync<Dictionary<string, object>>(soql);

                if (result?.records != null && result.records.Count > 0)
                {
                    var record = result.records[0];
                    Console.WriteLine($"[AuthService] Found Contact record for PIN in Salesforce");

                    // Convert Salesforce record to LocalUser
                    var localUser = new LocalUser
                    {
                        Id = record["Id"]?.ToString(),
                        Name = record["Name"]?.ToString(),
                        PIN = record["PIN__c"]?.ToString(),
                        IsActive = record["IsActive__c"] != null &&
                                  bool.TryParse(record["IsActive__c"].ToString(), out bool isActive) && isActive,
                        Permissions = record["Permissions__c"]?.ToString() ?? "STOCK_TAKE",
                        LastSyncDate = DateTime.UtcNow
                    };

                    // Validate required fields
                    if (!string.IsNullOrEmpty(localUser.Id) &&
                        !string.IsNullOrEmpty(localUser.Name) &&
                        !string.IsNullOrEmpty(localUser.PIN))
                    {
                        // Save/update ONLY this user in local database
                        await _dbService.SaveUserAsync(localUser);
                        Console.WriteLine($"[AuthService] Synced user to local database: {localUser.Name}");

                        return localUser;
                    }
                    else
                    {
                        Console.WriteLine($"[AuthService] Invalid user record from Salesforce: ID={localUser.Id}, Name={localUser.Name}, PIN={localUser.PIN}");
                        return null;
                    }
                }
                else
                {
                    Console.WriteLine("[AuthService] No Contact record found with the provided PIN");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AuthService] Error authenticating against Salesforce: {ex.Message}");
                Console.WriteLine($"[AuthService] Stack trace: {ex.StackTrace}");
                throw; // Re-throw to let caller handle
            }
        }

        /// <summary>
        /// Syncs users from Salesforce Contact records to local database (ADMIN USE ONLY)
        /// </summary>
        private async Task SyncUsersFromSalesforceAsync()
        {
            try
            {
                Console.WriteLine("[AuthService] Starting ADMIN user sync from Salesforce");

                // Ensure Salesforce connection
                await _salesforceService.EnsureAuthenticatedAsync();

                // Query Contact records with PIN and active status
                string soql = @"SELECT Id, Name, PIN__c, IsActive__c, Permissions__c, LastModifiedDate 
                               FROM Contact 
                               WHERE PIN__c != null 
                               AND PIN__c != '' 
                               ORDER BY LastModifiedDate DESC";

                Console.WriteLine($"[AuthService] Executing SOQL: {soql}");

                var result = await _salesforceService.ExecuteQueryAsync<Dictionary<string, object>>(soql);

                if (result?.records != null && result.records.Count > 0)
                {
                    Console.WriteLine($"[AuthService] Found {result.records.Count} Contact records with PINs");

                    int syncedCount = 0;
                    foreach (var record in result.records)
                    {
                        try
                        {
                            // Convert Salesforce record to LocalUser
                            var localUser = new LocalUser
                            {
                                Id = record["Id"]?.ToString(),
                                Name = record["Name"]?.ToString(),
                                PIN = record["PIN__c"]?.ToString(),
                                IsActive = record["IsActive__c"] != null &&
                                          bool.TryParse(record["IsActive__c"].ToString(), out bool isActive) && isActive,
                                Permissions = record["Permissions__c"]?.ToString() ?? "STOCK_TAKE",
                                LastSyncDate = DateTime.UtcNow
                            };

                            // Validate required fields
                            if (!string.IsNullOrEmpty(localUser.Id) &&
                                !string.IsNullOrEmpty(localUser.Name) &&
                                !string.IsNullOrEmpty(localUser.PIN))
                            {
                                // Save/update in local database
                                await _dbService.SaveUserAsync(localUser);
                                syncedCount++;
                                Console.WriteLine($"[AuthService] Synced user: {localUser.Name} (PIN: {localUser.PIN})");
                            }
                            else
                            {
                                Console.WriteLine($"[AuthService] Skipped invalid record: ID={localUser.Id}, Name={localUser.Name}, PIN={localUser.PIN}");
                            }
                        }
                        catch (Exception recordEx)
                        {
                            Console.WriteLine($"[AuthService] Error processing user record: {recordEx.Message}");
                        }
                    }

                    Console.WriteLine($"[AuthService] ADMIN user sync completed: {syncedCount} users synchronized");
                }
                else
                {
                    Console.WriteLine("[AuthService] No Contact records found with PINs");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AuthService] Error syncing users from Salesforce: {ex.Message}");
                Console.WriteLine($"[AuthService] Stack trace: {ex.StackTrace}");
                throw; // Re-throw to let caller handle
            }
        }

        /// <summary>
        /// Updates last login date in Salesforce Contact record (background task)
        /// </summary>
        private async Task UpdateLastLoginAsync(LocalUser user)
        {
            try
            {
                Console.WriteLine($"[AuthService] Updating last login for user {user.Name} in Salesforce");

                // Prepare update data
                var updateData = new Dictionary<string, object>
                {
                    {"LastLoginDate__c", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")},
                    {"DeviceId__c", DeviceInfo.Name ?? "Unknown Device"}
                };

                // Update Contact record in Salesforce
                await _salesforceService.UpdateRecordAsync("Contact", user.Id, updateData);

                Console.WriteLine($"[AuthService] Successfully updated last login for {user.Name}");
            }
            catch (Exception ex)
            {
                // Don't throw - this is a background operation
                Console.WriteLine($"[AuthService] Error updating last login: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if current user has a specific permission
        /// </summary>
        public bool HasPermission(string permission)
        {
            return _currentUser?.HasPermission(permission) ?? false;
        }

        /// <summary>
        /// Logs out the current user
        /// </summary>
        public async Task LogoutAsync()
        {
            try
            {
                Console.WriteLine($"[AuthService] Logging out user: {_currentUser?.Name ?? "Unknown"}");
                _currentUser = null;

                Console.WriteLine("[AuthService] User logged out successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AuthService] Error during logout: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if user is currently authenticated
        /// </summary>
        public bool IsAuthenticated => _currentUser != null;

        /// <summary>
        /// Manually syncs users from Salesforce (for testing/admin use)
        /// </summary>
        public async Task<int> SyncUsersAsync()
        {
            try
            {
                // Check for network connectivity
                if (Connectivity.NetworkAccess != NetworkAccess.Internet)
                {
                    Console.WriteLine("[AuthService] No network connectivity for user sync");
                    return 0;
                }

                var usersBefore = await _dbService.GetAllUsersAsync();
                int countBefore = usersBefore?.Count ?? 0;

                await SyncUsersFromSalesforceAsync();

                var usersAfter = await _dbService.GetAllUsersAsync();
                int countAfter = usersAfter?.Count ?? 0;

                int syncedCount = countAfter; // Total users in database after sync
                Console.WriteLine($"[AuthService] Manual user sync completed: {syncedCount} total users available");

                return syncedCount;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AuthService] Error in manual user sync: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Manually adds a user to local database (for testing/setup)
        /// </summary>
        public async Task<bool> AddUserAsync(string id, string name, string pin, string permissions = "STOCK_TAKE")
        {
            try
            {
                var user = new LocalUser
                {
                    Id = id,
                    Name = name,
                    PIN = pin,
                    IsActive = true,
                    Permissions = permissions,
                    LastSyncDate = DateTime.UtcNow
                };

                await _dbService.SaveUserAsync(user);
                Console.WriteLine($"[AuthService] Added user: {name} with PIN: {pin}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AuthService] Error adding user: {ex.Message}");
                return false;
            }
        }
    }
}