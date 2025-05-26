using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RenewitSalesforceApp.Helpers
{
    public static class BackgroundHelper
    {
        public static void RunWithoutBlockingUI(Func<Task> action)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(100);
                    await action();
                }
                catch (ObjectDisposedException)
                {
                    Console.WriteLine("Ignored ObjectDisposedException in background task");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in background task: {ex.Message}");
                }
            });
        }
    }
}
