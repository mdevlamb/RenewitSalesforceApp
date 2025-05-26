using System;
using SQLite;

namespace RenewitSalesforceApp.Models
{
    public class LocalUser
    {
        [PrimaryKey]
        public string Id { get; set; }                    // Salesforce Contact ID
        public string Name { get; set; }                  // Contact Name
        public string PIN { get; set; }                   // PIN__c
        public bool IsActive { get; set; }                // IsActive__c
        public string Permissions { get; set; }           // Permissions__c (comma-separated)
        public DateTime LastSyncDate { get; set; }        // When contact data was last synced from SF

        public bool HasPermission(string permission)
        {
            if (string.IsNullOrEmpty(Permissions))
                return false;

            return Permissions.Contains(permission, StringComparison.OrdinalIgnoreCase);
        }
    }
}