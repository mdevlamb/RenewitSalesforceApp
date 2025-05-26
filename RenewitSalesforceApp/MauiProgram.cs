using Microsoft.Extensions.Logging;
using RenewitSalesforceApp.Views;
using RenewitSalesforceApp.Services;
using ZXing.Net.Maui;
using ZXing.Net.Maui.Controls;

namespace RenewitSalesforceApp
{
    public static class MauiProgram
    {
        private const bool USE_PRODUCTION_SALESFORCE = false;

        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseBarcodeReader()  // Add this for ZXing
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("MaterialIcons-Regular.ttf", "MaterialIcons");
                });

            // Database path setup
            string dbPath = Path.Combine(FileSystem.AppDataDirectory, "renewit_local.db3");
            Console.WriteLine($"DB path: {dbPath}");

            // Register services
            builder.Services.AddSingleton<SalesforceService>(sp =>
                new SalesforceService(isProd: USE_PRODUCTION_SALESFORCE));

            builder.Services.AddSingleton<LocalDatabaseService>(sp =>
                new LocalDatabaseService(dbPath));

            builder.Services.AddSingleton<AuthService>();

            // Add the key service for stock takes
            builder.Services.AddTransient<StockTakeService>();
            builder.Services.AddSingleton<SyncService>();

            // Register pages
            builder.Services.AddTransient<StockTakePage>();

            // Initialize database asynchronously
            var dbInitTask = Task.Run(async () => {
                var dbService = builder.Services.BuildServiceProvider().GetService<LocalDatabaseService>();
                if (dbService != null)
                {
                    await dbService.InitializeAsync();
                    Console.WriteLine("Database initialized asynchronously");
                }
            });

            App.DatabaseInitializationTask = dbInitTask;

#if DEBUG
            builder.Logging.AddDebug();
#endif
            return builder.Build();
        }
    }
}