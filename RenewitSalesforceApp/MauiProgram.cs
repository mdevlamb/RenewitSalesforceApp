using Microsoft.Extensions.Logging;
using RenewitSalesforceApp.Views;
using RenewitSalesforceApp.Services;
using RenewitSalesforceApp.Helpers;

namespace RenewitSalesforceApp
{
    public static class MauiProgram
    {
        private const bool USE_PRODUCTION_SALESFORCE = false;

        public static MauiApp CreateMauiApp()
        {
            var startTime = DateTime.Now;
            var builder = MauiApp.CreateBuilder();

            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                    fonts.AddFont("MaterialIcons-Regular.ttf", "MaterialIcons");
                });

            try
            {
                Console.WriteLine("Registering services in DI container");

                // Database path setup
                string dbPath = SqlitePath.GetPath("renewit.db3");
                Console.WriteLine($"Database path: {dbPath}");

                // Register core services
                builder.Services.AddSingleton<LocalDatabaseService>(sp =>
                    new LocalDatabaseService(dbPath));

                // Add SalesforceService first
                builder.Services.AddSingleton<SalesforceService>(sp =>
                    new SalesforceService(isProd: USE_PRODUCTION_SALESFORCE));

                // Add AuthService with both dependencies
                builder.Services.AddSingleton<AuthService>(sp =>
                    new AuthService(
                        sp.GetRequiredService<LocalDatabaseService>(),
                        sp.GetRequiredService<SalesforceService>()
                    ));

                // Register pages as transient (new instance each time)
                builder.Services.AddTransient<PinLoginPage>();
                builder.Services.AddTransient<HomePage>();

                // Initialize the database in a background task
                var dbInitTask = Task.Run(async () => {
                    try
                    {
                        var dbService = builder.Services.BuildServiceProvider().GetService<LocalDatabaseService>();
                        if (dbService != null)
                        {
                            await dbService.InitializeAsync();
                            Console.WriteLine("Database initialized asynchronously");

                            // Add a test user for development (remove in production)
                            var authService = builder.Services.BuildServiceProvider().GetService<AuthService>();
                            if (authService != null)
                            {
                                await authService.AddUserAsync(
                                    id: "test001",
                                    name: "Test User",
                                    pin: "1234",
                                    permissions: "STOCK_TAKE"
                                );
                                Console.WriteLine("Test user added: PIN=1234");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error during database initialization: {ex.Message}");
                    }
                });

                // Store this task so it can be awaited later if needed
                App.DatabaseInitializationTask = dbInitTask;

                Console.WriteLine($"Service registration completed in {(DateTime.Now - startTime).TotalMilliseconds}ms");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error registering services: {ex.Message}");
            }

#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}