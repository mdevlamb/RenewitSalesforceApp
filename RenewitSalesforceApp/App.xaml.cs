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

            // Initialize services when app starts
            window.Created += async (s, e) =>
            {
                try
                {
                    // Wait a moment for services to be ready
                    await Task.Delay(1000);

                    // Get services from DI container
                    var services = Handler?.MauiContext?.Services;
                    if (services != null)
                    {
                        // Initialize UpdateService
                        var updateService = services.GetService<UpdateService>();
                        if (updateService != null)
                        {
                            Console.WriteLine("[App] UpdateService initialized for update checking");
                            // Trigger update check (fire and forget)
                            _ = updateService.CheckForUpdates();
                        }
                        else
                        {
                            Console.WriteLine("[App] ERROR: UpdateService not found in DI container");
                        }
                    }
                    else
                    {
                        Console.WriteLine("[App] ERROR: Services not available in MauiContext");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[App] Error initializing services: {ex.Message}");
                }
            };

            return window;
        }
    }
}