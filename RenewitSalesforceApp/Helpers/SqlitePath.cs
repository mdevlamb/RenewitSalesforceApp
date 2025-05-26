using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RenewitSalesforceApp.Helpers
{
    public static class SqlitePath
    {
        public static string GetPath(string nameDb)
        {
            var pathDbSqlite = nameDb;

            try
            {
                if (DeviceInfo.Platform == DevicePlatform.Android)
                {
                    pathDbSqlite = Path.Combine(FileSystem.AppDataDirectory, nameDb);
                    Console.WriteLine($"Android DB path: {pathDbSqlite}");
                }
                else if (DeviceInfo.Platform == DevicePlatform.iOS)
                {
                    pathDbSqlite = Path.Combine(FileSystem.AppDataDirectory, nameDb);
                    Console.WriteLine($"iOS DB path: {pathDbSqlite}");
                }
                else
                {
                    pathDbSqlite = Path.Combine(FileSystem.AppDataDirectory, nameDb);
                    Console.WriteLine($"Other platform DB path: {pathDbSqlite}");
                }

                string directory = Path.GetDirectoryName(pathDbSqlite);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    Console.WriteLine($"Created directory: {directory}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetPath: {ex.Message}");
                pathDbSqlite = Path.Combine(FileSystem.AppDataDirectory, nameDb);
                Console.WriteLine($"Fallback DB path: {pathDbSqlite}");
            }

            return pathDbSqlite;
        }
    }
}
