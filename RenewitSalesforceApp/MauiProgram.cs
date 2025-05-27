using Microsoft.Extensions.Logging;
using RenewitSalesforceApp.Services;
using RenewitSalesforceApp.Views;
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
                .UseBarcodeReader()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("MaterialIcons-Regular.ttf", "MaterialIcons");
                });

            // Database path setup
            string dbPath = Path.Combine(FileSystem.AppDataDirectory, "renewit_local.db3");
            Console.WriteLine($"DB path: {dbPath}");

            builder.Services.AddSingleton<UpdateService>();

            // Register services in correct order
            builder.Services.AddSingleton<SalesforceService>(sp =>
                new SalesforceService(isProd: USE_PRODUCTION_SALESFORCE));

            builder.Services.AddSingleton<LocalDatabaseService>(sp =>
                new LocalDatabaseService(dbPath));

            builder.Services.AddSingleton<AuthService>();

            // IMPORTANT: Change StockTakeService to Singleton so SyncService can use it
            builder.Services.AddSingleton<StockTakeService>();

            // SyncService depends on StockTakeService, so register after
            builder.Services.AddSingleton<SyncService>();

            // Register pages as transient
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