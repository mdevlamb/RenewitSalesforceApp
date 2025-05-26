using RenewitSalesforceApp.Views;
using RenewitSalesforceApp.Services;

namespace RenewitSalesforceApp
{
    public partial class App : Application
    {
        public static Task DatabaseInitializationTask { get; set; }

        public App()
        {
            InitializeComponent();

            MainPage = new NavigationPage(new PinLoginPage());
        }

        protected override Window CreateWindow(IActivationState activationState)
        {
            var window = base.CreateWindow(activationState);

            // Initialize SyncService when app starts
            window.Created += async (s, e) =>
            {
                try
                {
                    // Wait a moment for services to be ready
                    await Task.Delay(1000);

                    // Get and initialize SyncService from DI container
                    var services = Handler?.MauiContext?.Services;
                    if (services != null)
                    {
                        var syncService = services.GetService<SyncService>();
                        if (syncService != null)
                        {
                            Console.WriteLine("[App] SyncService initialized for auto-sync");

                            // Trigger initial sync check
                            _ = Task.Run(async () => await syncService.TriggerSyncAsync());
                        }
                        else
                        {
                            Console.WriteLine("[App] ERROR: SyncService not found in DI container");
                        }
                    }
                    else
                    {
                        Console.WriteLine("[App] ERROR: Services not available in MauiContext");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[App] Error initializing SyncService: {ex.Message}");
                }
            };

            return window;
        }
    }
}