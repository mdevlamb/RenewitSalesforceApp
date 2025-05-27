using System;
using SQLite;

namespace RenewitSalesforceApp.Models
{
    public class StockTakeRecord
    {
        [PrimaryKey, AutoIncrement]
        public int LocalId { get; set; }

        // Salesforce ID (populated after sync)
        public string Id { get; set; }

        // Salesforce Name field (auto-generated SF record ID)
        public string Name { get; set; }

        // Main identification fields
        public string DISC_REG__c { get; set; }           // Full license disk barcode data (SF)
        public string Vehicle_Registration__c { get; set; } // Extracted vehicle registration number (SF)
        public string License_Number__c { get; set; }     // License/Registration Number (SF)
        public string REFID__c { get; set; }              // Reference ID - auto-populated with datetime (SF)
        // public string Ref_No__c { get; set; }          // Reference Number - not needed

        // Vehicle details extracted from barcode scan
        public string Make__c { get; set; }               // Vehicle make (e.g., VOLKSWAGEN) (SF)
        public string Model__c { get; set; }              // Vehicle model (e.g., VW 216 T-CROSS) (SF)
        public string Colour__c { get; set; }             // Vehicle colour (e.g., Black / Swart) (SF)
        public string Vehicle_Type__c { get; set; }       // Vehicle type (e.g., Hatch back / Luikrug) (SF)
        public string VIN__c { get; set; }                // VIN number (e.g., WVGZZZC1ZLY056933) (SF)
        public string Engine_Number__c { get; set; }      // Engine number (e.g., DKJ048733) (SF)
        public string License_Expiry_Date__c { get; set; } // License disk expiry date (e.g., 2025-05-31) (SF)

        // Location fields - matching Salesforce object structure
        public string Yards__c { get; set; }              // Branch Names picklist from Salesforce (SF)
        public string Yard_Location__c { get; set; }      // Department Location picklist from Salesforce (SF)

        // GPS and Location data
        public string Geo__c { get; set; }                // Salesforce Geolocation field (uses __Latitude__s and __Longitude__s subfields) (SF)
        public string GPS_CORD__c { get; set; }           // GPS coordinates as comma-separated string "latitude,longitude" (SF)
        public double? LocalLatitude { get; set; }        // Local-only latitude storage
        public double? LocalLongitude { get; set; }       // Local-only longitude storage

        // Comments and Notes
        public string Comments__c { get; set; }           // Comments (SF)
        public string Notes__c { get; set; }              // Notes - External ID (SF)

        // Stock take tracking - now Salesforce fields
        public string Stock_Take_Date__c { get; set; }    // When stock take was done - Date field in SF (SF)
        public string Stock_Take_By__c { get; set; }      // Who did the stock take - Text field in SF (SF)

        // Local tracking fields (for app use only)
        public DateTime LocalStockTakeDate { get; set; }  // Local DateTime for app logic
        public string LocalStockTakeBy { get; set; }      // Local user info for app logic

        // Photo information (local fields only - not synced to SF)
        public bool HasPhoto { get; set; }                // Whether photos were taken (Local)
        public int PhotoCount { get; set; }               // Number of photos (Local)
        public string PhotoPath { get; set; }             // Primary photo path (Local)
        public string AllPhotoPaths { get; set; }         // All photo paths (semicolon separated) (Local)

        // Sync tracking (local only)
        [Indexed]
        public bool IsSynced { get; set; }
        public DateTime? SyncTimestamp { get; set; }
        public int SyncAttempts { get; set; }
        public string SyncErrorMessage { get; set; }

        // Helper methods
        /// <summary>
        /// Sets GPS coordinates for both local storage and Salesforce fields
        /// </summary>
        public void SetGPSCoordinates(double? latitude, double? longitude)
        {
            LocalLatitude = latitude;
            LocalLongitude = longitude;

            if (latitude.HasValue && longitude.HasValue)
            {
                // For GPS_CORD__c (simple comma-separated string)
                GPS_CORD__c = $"{latitude.Value:F6},{longitude.Value:F6}";

                // For Geo__c (Salesforce geolocation field - formatted for API)
                Geo__c = null;
            }
        }

        /// <summary>
        /// Sets the stock take date for both local and Salesforce fields
        /// </summary>
        public void SetStockTakeDate(DateTime dateTime)
        {
            // Use South Africa timezone
            var southAfricaTime = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(dateTime, "South Africa Standard Time");
            Stock_Take_Date__c = southAfricaTime.ToString("yyyy-MM-dd");
            Console.WriteLine($"[StockTakeRecord] Set Stock Take Date: {Stock_Take_Date__c} at {southAfricaTime}");
        }

        /// <summary>
        /// Sets the stock take user for both local and Salesforce fields
        /// </summary>
        public void SetStockTakeBy(string userName)
        {
            LocalStockTakeBy = userName;
            Stock_Take_By__c = userName;
        }

        /// <summary>
        /// Auto-generates REFID with current timestamp
        /// </summary>
        public void GenerateRefId()
        {
            // Use South Africa timezone for consistent timestamps
            var southAfricaTime = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.Now, "South Africa Standard Time");
            REFID__c = southAfricaTime.ToString("yyyyMMddHHmmss");
            Console.WriteLine($"[StockTakeRecord] Generated REFID: {REFID__c} at {southAfricaTime}");
        }

        // Helper properties
        public string DisplayName => !string.IsNullOrEmpty(Vehicle_Registration__c) ?
            $"Registration: {Vehicle_Registration__c}" :
            (!string.IsNullOrEmpty(License_Number__c) ? $"License: {License_Number__c}" : $"Ref: {REFID__c}");

        public string VehicleInfo => !string.IsNullOrEmpty(Make__c) && !string.IsNullOrEmpty(Model__c) ?
            $"{Make__c} {Model__c}" : "Vehicle details not scanned";

        public string LocationInfo => !string.IsNullOrEmpty(Yards__c) && !string.IsNullOrEmpty(Yard_Location__c) ?
            $"{Yards__c} - {Yard_Location__c}" : "Location not set";
    }

    // Fallback offline values - these will be used if Salesforce is unavailable
    public static class YardNames
    {
        public const string Bedfordview = "Renew-it Bedfordview";
        public const string Randburg = "Renew-it Randburg";
        public const string Sandton = "Renew-it Sandton";
        public const string Proline = "Renew-it Proline";
        public const string DCP = "Renew-it DCP";
        public const string Technostar = "Renew-it Technostar";
        public const string Greenstone = "Renew-it Greenstone";
        public const string ProlineExpressSandhurst = "Renew-It Proline Express Sandhurst";
        public const string Rivonia = "Renew-it Rivonia";
        public const string Umlhanga = "Renew-It Umlhanga";

        public static readonly string[] All = {
            Bedfordview, Randburg, Sandton, Proline, DCP,
            Technostar, Greenstone, ProlineExpressSandhurst, Rivonia, Umlhanga
        };
    }

    public static class YardLocations
    {
        public const string Topyard = "Topyard";
        public const string InsurerYard = "Insurer Yard";
        public const string Ready = "Ready";
        public const string AwaitingPanel = "Awaiting Panel";
        public const string Assembly = "Assembly";
        public const string PanelBeating = "Panel Beating";
        public const string PrepBay = "Prep Bay";
        public const string Paintshop = "Paintshop";
        public const string Loading = "Loading";
        public const string Mechanical = "Mechanical";
        public const string FinnishingBay = "Finnishing Bay";
        public const string SprayBooth = "Spray Booth";
        public const string QualityInspection = "Quality Inspection";
        public const string PolishBay = "Polish Bay";
        public const string WheelAlignment = "Wheel Alignment";
        public const string WashBay = "Wash Bay";
        public const string Prepbay = "Prepbay";

        public static readonly string[] All = {
            Topyard, InsurerYard, Ready, AwaitingPanel, Assembly,
            PanelBeating, PrepBay, Paintshop, Loading, Mechanical,
            FinnishingBay, SprayBooth, QualityInspection, PolishBay,
            WheelAlignment, WashBay, Prepbay
        };
    }
}