using RenewitSalesforceApp.Helpers;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RenewitSalesforceApp.Models;

namespace RenewitSalesforceApp.Services
{
    public class SalesforceService
    {
        // Configuration properties
        private readonly bool _isProd;
        private string _clientId;
        private string _clientSecret;

        // URLs for different environments - UPDATE THESE WITH YOUR RENEWIT SALESFORCE URLS
        private readonly string _prodTokenUrl = "https://login.salesforce.com/services/oauth2/token";
        private readonly string _sandboxTokenUrl = "https://renewit--copyrenew.sandbox.my.salesforce.com/services/oauth2/token";

        // Service state
        private string _accessToken;
        private string _instanceUrl;
        private string _idUrl;
        private string _signature;
        private string _tokenType;
        private DateTime _issuedAt;
        private DateTime _tokenExpiry;
        private readonly HttpClient _httpClient;

        // File path for token cache
        private readonly string _tokenCacheFile;

        public string InstanceUrl => _instanceUrl;
        public string AccessToken => _accessToken;

        public SalesforceService(bool isProd = false)
        {
            _isProd = isProd;
            _httpClient = new HttpClient();
            _tokenCacheFile = Path.Combine(FileSystem.CacheDirectory, "renewit_sf_token_cache.json");

            // Initialize credentials asynchronously
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await InitializeCredentialsAsync();
                LoadCachedToken();
            });
        }

        private async Task InitializeCredentialsAsync()
        {
            try
            {
                // Try to get credentials from secure storage
                string clientIdKey = _isProd ? "RenewitProdClientId" : "RenewitSandboxClientId";
                string clientSecretKey = _isProd ? "RenewitProdClientSecret" : "RenewitSandboxClientSecret";

                _clientId = await SecureStorage.GetAsync(clientIdKey);
                _clientSecret = await SecureStorage.GetAsync(clientSecretKey);

                // If we don't have stored credentials yet, use defaults and save them
                if (string.IsNullOrEmpty(_clientId) || string.IsNullOrEmpty(_clientSecret))
                {
                    // Set default values - REPLACE THESE WITH YOUR ACTUAL RENEWIT CREDENTIALS
                    _clientId = _isProd
                        ? ""  // Your Renewit prod client ID
                        : "3MVG9f4Sg6xGHZYr8NWbJAKo7ETI68zkGk6o0OYj7gEyu8NYSIYbdFNroXUVmzexy2MxoW0e8cd_60ft.o_LU";  // Your Renewit sandbox client ID

                    _clientSecret = _isProd
                        ? ""  // Your Renewit prod client secret
                        : "19A540E088639F8561122A976551D02791354166D9A200509A081B730BC77630";  // Your Renewit sandbox client secret

                    // Store in secure storage for next time
                    await SecureStorage.SetAsync(clientIdKey, _clientId);
                    await SecureStorage.SetAsync(clientSecretKey, _clientSecret);

                    Console.WriteLine($"Renewit credentials stored securely for {(_isProd ? "Production" : "Sandbox")}");
                }
                else
                {
                    Console.WriteLine($"Renewit credentials loaded securely for {(_isProd ? "Production" : "Sandbox")}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing Renewit credentials: {ex.Message}");

                // Fallback to default values if secure storage fails
                _clientId = _isProd
                    ? "YOUR_RENEWIT_PROD_CLIENT_ID"
                    : "YOUR_RENEWIT_SANDBOX_CLIENT_ID";

                _clientSecret = _isProd
                    ? "YOUR_RENEWIT_PROD_CLIENT_SECRET"
                    : "YOUR_RENEWIT_SANDBOX_CLIENT_SECRET";
            }
        }

        private void LoadCachedToken()
        {
            try
            {
                if (File.Exists(_tokenCacheFile))
                {
                    var json = File.ReadAllText(_tokenCacheFile);
                    var cachedToken = JsonSerializer.Deserialize<SalesforceTokenCache>(json);

                    if (cachedToken != null &&
                        DateTime.UtcNow < cachedToken.ExpiryTime &&
                        !string.IsNullOrEmpty(cachedToken.AccessToken))
                    {
                        // Token is still valid, restore it
                        _accessToken = cachedToken.AccessToken;
                        _instanceUrl = cachedToken.InstanceUrl;
                        _idUrl = cachedToken.IdUrl;
                        _signature = cachedToken.Signature;
                        _tokenType = cachedToken.TokenType;
                        _issuedAt = cachedToken.IssuedAt;
                        _tokenExpiry = cachedToken.ExpiryTime;

                        Console.WriteLine("Loaded cached Renewit Salesforce token, valid until: " + _tokenExpiry);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading cached Renewit token: {ex.Message}");
            }
        }

        private void SaveTokenToCache()
        {
            try
            {
                var tokenCache = new SalesforceTokenCache
                {
                    AccessToken = _accessToken,
                    InstanceUrl = _instanceUrl,
                    IdUrl = _idUrl,
                    Signature = _signature,
                    TokenType = _tokenType,
                    IssuedAt = _issuedAt,
                    ExpiryTime = _tokenExpiry
                };

                var json = JsonSerializer.Serialize(tokenCache);
                File.WriteAllText(_tokenCacheFile, json);

                Console.WriteLine("Cached Renewit Salesforce token to file");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error caching Renewit token: {ex.Message}");
            }
        }

        private async Task EnsureAuthenticated()
        {
            if (string.IsNullOrEmpty(_accessToken) || DateTime.UtcNow >= _tokenExpiry)
            {
                await Authenticate();
            }
        }

        private async Task Authenticate()
        {
            try
            {
                var tokenUrl = _isProd ? _prodTokenUrl : _sandboxTokenUrl;

                var formContent = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    { "grant_type", "client_credentials" },
                    { "client_id", _clientId },
                    { "client_secret", _clientSecret }
                });

                var response = await _httpClient.PostAsync(tokenUrl, formContent);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    throw new Exception($"Renewit Salesforce authentication failed: {response.StatusCode}, {errorContent}");
                }

                var responseJson = await response.Content.ReadAsStringAsync();
                var authResponse = JsonSerializer.Deserialize<SalesforceAuthResponse>(responseJson);

                _accessToken = authResponse.access_token;
                _instanceUrl = authResponse.instance_url;
                _idUrl = authResponse.id;
                _signature = authResponse.signature;
                _tokenType = authResponse.token_type;

                if (long.TryParse(authResponse.issued_at, out long issuedAtMs))
                {
                    _issuedAt = DateTimeOffset.FromUnixTimeMilliseconds(issuedAtMs).UtcDateTime;
                }
                else
                {
                    _issuedAt = DateTime.UtcNow;
                }

                _tokenExpiry = _issuedAt.AddHours(2).AddMinutes(-5);

                // Cache the token
                SaveTokenToCache();

                Console.WriteLine($"Successfully authenticated with Renewit Salesforce. Token expires at {_tokenExpiry}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Renewit Salesforce authentication error: {ex.Message}");
                throw;
            }
        }

        public async Task EnsureAuthenticatedAsync()
        {
            Console.WriteLine("RenewitSalesforceService.EnsureAuthenticatedAsync called");

            if (string.IsNullOrEmpty(_accessToken) || DateTime.UtcNow >= _tokenExpiry)
            {
                Console.WriteLine("RenewitSalesforceService not authenticated or token expired, authenticating now");
                await Authenticate();
                Console.WriteLine($"RenewitSalesforceService authentication completed. Token expires at {_tokenExpiry}");
            }
            else
            {
                Console.WriteLine("RenewitSalesforceService already authenticated");
            }
        }

        /// <summary>
        /// Creates a record in Salesforce
        /// </summary>
        public async Task<string> CreateRecordAsync<T>(string objectName, T record)
        {
            await EnsureAuthenticated();

            try
            {
                string url = $"{_instanceUrl}/services/data/v58.0/sobjects/{objectName}";

                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(_tokenType, _accessToken);

                var json = JsonSerializer.Serialize(record);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                Console.WriteLine($"[RenewitSalesforceService] Creating {objectName} record");
                Console.WriteLine($"[RenewitSalesforceService] JSON: {json}");

                var response = await _httpClient.PostAsync(url, content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    throw new Exception($"Renewit Salesforce record creation failed: {response.StatusCode}, {errorContent}");
                }

                var responseJson = await response.Content.ReadAsStringAsync();
                var createResponse = JsonSerializer.Deserialize<SalesforceCreateResponse>(responseJson);

                Console.WriteLine($"[RenewitSalesforceService] Successfully created {objectName} with ID: {createResponse.id}");
                return createResponse.id;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Renewit Salesforce create error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Updates a record in Salesforce
        /// </summary>
        public async Task UpdateRecordAsync<T>(string objectName, string recordId, T record)
        {
            await EnsureAuthenticated();

            try
            {
                string url = $"{_instanceUrl}/services/data/v58.0/sobjects/{objectName}/{recordId}";

                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(_tokenType, _accessToken);

                var json = JsonSerializer.Serialize(record);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(new HttpMethod("PATCH"), url)
                {
                    Content = content
                };

                Console.WriteLine($"[RenewitSalesforceService] Updating {objectName} record ID: {recordId}");
                Console.WriteLine($"[RenewitSalesforceService] JSON: {json}");

                var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    throw new Exception($"Renewit Salesforce record update failed: {response.StatusCode}, {errorContent}");
                }

                Console.WriteLine($"[RenewitSalesforceService] Successfully updated {objectName} ID: {recordId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Renewit Salesforce update error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Uploads a file to Salesforce as ContentVersion
        /// </summary>
        public async Task<string> UploadFileAsync(string parentId, string fileName, byte[] fileData, string contentType)
        {
            await EnsureAuthenticated();

            try
            {
                var contentVersion = new
                {
                    Title = fileName,
                    PathOnClient = fileName,
                    VersionData = Convert.ToBase64String(fileData),
                    FirstPublishLocationId = parentId
                };

                Console.WriteLine($"[RenewitSalesforceService] Uploading file: {fileName} ({fileData.Length} bytes)");
                var fileId = await CreateRecordAsync("ContentVersion", contentVersion);
                Console.WriteLine($"[RenewitSalesforceService] File uploaded with ID: {fileId}");

                return fileId;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Renewit Salesforce file upload error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Executes a SOQL query (for user sync)
        /// </summary>
        public async Task<SalesforceQueryResult<T>> ExecuteQueryAsync<T>(string soql) where T : class
        {
            await EnsureAuthenticated();

            try
            {
                string encodedQuery = Uri.EscapeDataString(soql);
                string url = $"{_instanceUrl}/services/data/v58.0/query?q={encodedQuery}";

                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(_tokenType, _accessToken);

                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    throw new Exception($"Renewit Salesforce query failed: {response.StatusCode}, {errorContent}");
                }

                var responseJson = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<SalesforceQueryResult<T>>(responseJson);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Renewit Salesforce query error: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Gets picklist values for a specific field on a Salesforce object
        /// </summary>
        /// <param name="objectName">Salesforce object name (e.g., "Yard_Stock_Take__c")</param>
        /// <param name="fieldName">Field name (e.g., "Yards__c")</param>
        /// <returns>List of picklist values</returns>
        public async Task<List<string>> GetPicklistValues(string objectName, string fieldName)
        {
            try
            {
                Console.WriteLine($"[SalesforceService] Getting picklist values for {objectName}.{fieldName}");

                if (string.IsNullOrEmpty(AccessToken))
                {
                    Console.WriteLine("[SalesforceService] No access token available");
                    return null;
                }

                var client = new HttpClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);

                // Use Salesforce REST API to describe the object
                var describeUrl = $"{InstanceUrl}/services/data/v58.0/sobjects/{objectName}/describe";

                Console.WriteLine($"[SalesforceService] Calling: {describeUrl}");

                var response = await client.GetAsync(describeUrl);
                var content = await response.Content.ReadAsStringAsync();

                Console.WriteLine($"[SalesforceService] Response status: {response.StatusCode}");
                Console.WriteLine($"[SalesforceService] Response content: {content.Substring(0, Math.Min(500, content.Length))}...");

                if (response.IsSuccessStatusCode)
                {
                    var describeResult = JsonSerializer.Deserialize<SalesforceObjectDescribe>(content);

                    // Find the specific field
                    var field = describeResult?.fields?.FirstOrDefault(f =>
                        string.Equals(f.name, fieldName, StringComparison.OrdinalIgnoreCase));

                    if (field?.picklistValues != null && field.picklistValues.Any())
                    {
                        var picklistValues = field.picklistValues
                            .Where(pv => pv.active)
                            .Select(pv => pv.value)
                            .ToList();

                        Console.WriteLine($"[SalesforceService] Found {picklistValues.Count} active picklist values for {fieldName}");
                        return picklistValues;
                    }
                    else
                    {
                        Console.WriteLine($"[SalesforceService] No picklist values found for field {fieldName}");
                        return new List<string>();
                    }
                }
                else
                {
                    Console.WriteLine($"[SalesforceService] Failed to get picklist values: {response.StatusCode} - {content}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SalesforceService] Error getting picklist values: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Creates a stock take record in Salesforce
        /// </summary>
        public async Task<string> CreateStockTakeRecord(StockTakeRecord stockTakeRecord)
        {
            try
            {
                Console.WriteLine($"[SalesforceService] Creating stock take record in Salesforce");

                await EnsureAuthenticated();

                string gpsCoordinates = string.IsNullOrWhiteSpace(stockTakeRecord.GPS_CORD__c)
                    ? "Unknown GPS"
                    : stockTakeRecord.GPS_CORD__c;

                // Map local record to Salesforce object
                var salesforceRecord = new
                {
                    DISC_REG__c = stockTakeRecord.DISC_REG__c,
                    //Vehicle_Registration__c = stockTakeRecord.Vehicle_Registration__c,
                    //License_Number__c = stockTakeRecord.License_Number__c,
                    REFID__c = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ssZ"),
                    //Make__c = stockTakeRecord.Make__c,
                    //Model__c = stockTakeRecord.Model__c,
                    //Colour__c = stockTakeRecord.Colour__c,
                    //Vehicle_Type__c = stockTakeRecord.Vehicle_Type__c,
                    //VIN__c = stockTakeRecord.VIN__c,
                    //Engine_Number__c = stockTakeRecord.Engine_Number__c,
                    //License_Expiry_Date__c = stockTakeRecord.License_Expiry_Date__c,
                    Yards__c = stockTakeRecord.Yards__c,
                    Yard_Location__c = stockTakeRecord.Yard_Location__c,
                    GPS_CORD__c = gpsCoordinates,
                    //Geo_Latitude__c = stockTakeRecord.Geo_Latitude__c,
                    //Geo_Longitude__c = stockTakeRecord.Geo_Longitude__c,
                    Comments__c = stockTakeRecord.Comments__c
                    //Stock_Take_Date__c = stockTakeRecord.Stock_Take_Date.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    //Stock_Take_By__c = stockTakeRecord.Stock_Take_By,
                    //Has_Photo__c = stockTakeRecord.Has_Photo,
                    //Photo_Count__c = stockTakeRecord.Photo_Count
                };

                Console.WriteLine($"[SalesforceService] Creating Yard_Stock_Take__c record");

                // Use your existing CreateRecordAsync method
                var recordId = await CreateRecordAsync("Yard_Stock_Take__c", salesforceRecord);

                Console.WriteLine($"[SalesforceService] Successfully created stock take record with ID: {recordId}");
                return recordId;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SalesforceService] Error creating stock take record: {ex.Message}");
                return null;
            }
        }
    }

    #region Response Models

    public class SalesforceAuthResponse
    {
        public string access_token { get; set; }
        public string signature { get; set; }
        public string instance_url { get; set; }
        public string id { get; set; }
        public string token_type { get; set; }
        public string issued_at { get; set; }
        public int expires_in { get; set; }
    }

    public class SalesforceTokenCache
    {
        public string AccessToken { get; set; }
        public string InstanceUrl { get; set; }
        public string IdUrl { get; set; }
        public string Signature { get; set; }
        public string TokenType { get; set; }
        public DateTime IssuedAt { get; set; }
        public DateTime ExpiryTime { get; set; }
    }

    public class SalesforceQueryResult<T> where T : class
    {
        public int totalSize { get; set; }
        public bool done { get; set; }
        public List<T> records { get; set; }
        public string nextRecordsUrl { get; set; }
    }

    public class SalesforceCreateResponse
    {
        public string id { get; set; }
        public bool success { get; set; }
        public List<SalesforceError> errors { get; set; }
    }

    public class SalesforceError
    {
        public string statusCode { get; set; }
        public string message { get; set; }
        public string fields { get; set; }
    }

    public class SalesforceObjectDescribe
    {
        public List<SalesforceField> fields { get; set; }
    }

    public class SalesforceField
    {
        public string name { get; set; }
        public string type { get; set; }
        public List<PicklistValue> picklistValues { get; set; }
    }

    public class PicklistValue
    {
        public bool active { get; set; }
        public string value { get; set; }
        public string label { get; set; }
    }

    #endregion
}