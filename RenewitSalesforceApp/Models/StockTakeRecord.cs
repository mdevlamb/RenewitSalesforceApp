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

        // Salesforce Name field (usually auto-generated)
        public string Name { get; set; }

        // Main identification fields
        public string DISC_REG__c { get; set; }           // Full license disk barcode data
        public string Vehicle_Registration__c { get; set; } // Extracted vehicle registration number
        public string License_Number__c { get; set; }     // License/Registration Number
        public string REFID__c { get; set; }              // Reference ID
        public string Ref_No__c { get; set; }             // Reference Number

        // Vehicle details extracted from barcode scan
        public string Make__c { get; set; }               // Vehicle make (e.g., VOLKSWAGEN)
        public string Model__c { get; set; }              // Vehicle model (e.g., VW 216 T-CROSS)
        public string Colour__c { get; set; }             // Vehicle colour (e.g., Black / Swart)
        public string Vehicle_Type__c { get; set; }       // Vehicle type (e.g., Hatch back / Luikrug)
        public string VIN__c { get; set; }                // VIN number (e.g., WVGZZZC1ZLY056933)
        public string Engine_Number__c { get; set; }      // Engine number (e.g., DKJ048733)
        public string License_Expiry_Date__c { get; set; } // License disk expiry date (e.g., 2025-05-31)

        // Location fields - matching Salesforce object structure
        public string Yards__c { get; set; }              // Yard Names picklist from Salesforce
        public string Yard_Location__c { get; set; }      // Yard Location picklist from Salesforce

        // GPS and Location data
        public string GPS_CORD__c { get; set; }           // GPS coordinates as text
        public double? Geo_Latitude__c { get; set; }      // Geolocation latitude
        public double? Geo_Longitude__c { get; set; }     // Geolocation longitude

        // Comments and Notes
        public string Comments__c { get; set; }           // Comments
        public string Notes__c { get; set; }              // Notes (External ID)

        // Stock take tracking (local fields)
        public DateTime Stock_Take_Date { get; set; }     // When stock take was done
        public string Stock_Take_By { get; set; }         // Who did the stock take

        // Photo information (local fields)
        public bool Has_Photo { get; set; }               // Whether photos were taken
        public int Photo_Count { get; set; }              // Number of photos
        public string PhotoPath { get; set; }             // Primary photo path
        public string AllPhotoPaths { get; set; }         // All photo paths (semicolon separated)

        // Sync tracking (local only)
        [Indexed]
        public bool IsSynced { get; set; }
        public DateTime? SyncTimestamp { get; set; }
        public int SyncAttempts { get; set; }
        public string SyncErrorMessage { get; set; }

        // Helper properties
        public string DisplayName => !string.IsNullOrEmpty(Vehicle_Registration__c) ?
            $"Registration: {Vehicle_Registration__c}" :
            (!string.IsNullOrEmpty(License_Number__c) ? $"License: {License_Number__c}" : $"Ref: {REFID__c}");

        public string VehicleInfo => !string.IsNullOrEmpty(Make__c) && !string.IsNullOrEmpty(Model__c) ?
            $"{Make__c} {Model__c}" : "Vehicle details not scanned";
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
        public const string NotYetAllocated = "NOT YET ALLOCATED";
        public const string Prepbay = "Prepbay";

        public static readonly string[] All = {
            Topyard, InsurerYard, Ready, AwaitingPanel, Assembly,
            PanelBeating, PrepBay, Paintshop, Loading, Mechanical,
            FinnishingBay, SprayBooth, QualityInspection, PolishBay,
            WheelAlignment, WashBay, NotYetAllocated, Prepbay
        };
    }
}