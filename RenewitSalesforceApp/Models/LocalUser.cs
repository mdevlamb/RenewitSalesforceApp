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
        public string BranchPermissions { get; set; }     // Allowed_Branches__c (comma-seperated)
        public DateTime LastSyncDate { get; set; }        // When contact data was last synced from SF

        public bool HasPermission(string permission)
        {
            if (string.IsNullOrEmpty(Permissions))
                return false;

            return Permissions.Contains(permission, StringComparison.OrdinalIgnoreCase);
        }

        public List<string> GetAllowedBranches()
        {
            if (string.IsNullOrEmpty(BranchPermissions))
                return new List<string>();

            return BranchPermissions.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                   .Select(b => b.Trim())
                                   .Where(b => !string.IsNullOrEmpty(b))
                                   .ToList();
        }

        public bool HasBranchPermission(string branchName)
        {
            var allowedBranches = GetAllowedBranches();
            return allowedBranches.Contains(branchName, StringComparer.OrdinalIgnoreCase);
        }
    }
}