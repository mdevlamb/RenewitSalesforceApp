using RenewitSalesforceApp.Views;

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

            window.Title = "Renewit Stock Take";

            return window;
        }
    }
}