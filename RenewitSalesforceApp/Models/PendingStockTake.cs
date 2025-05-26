using System;
using SQLite;

namespace RenewitSalesforceApp.Models
{
    public class PendingStockTake
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        // Basic identification
        [Indexed]
        public string ItemIdentifier { get; set; }        // DISC_REG__c or License_Number__c
        public string ItemType { get; set; }              // What type of item this is

        // User and timing
        public string UserId { get; set; }                // Who did the stock take
        public DateTime Timestamp { get; set; }           // When it was done

        // Location information
        public string YardName { get; set; }              // Which Renew-it location
        public string YardLocation { get; set; }          // Where in the yard

        // Additional data
        public string Notes { get; set; }                 // Any notes
        public string PhotoPath { get; set; }             // Photo if taken
        public string GpsCoordinates { get; set; }        // GPS coordinates

        // Sync status
        [Indexed]
        public bool IsSynced { get; set; }                // Whether synced to Salesforce
        public DateTime? SyncTimestamp { get; set; }      // When it was synced
        public string SyncErrorMessage { get; set; }      // Error if sync failed
    }
}