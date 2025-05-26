using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RenewitSalesforceApp.Converters
{
    public class ConnectivityIconConverter : IValueConverter
    {
        public static ConnectivityIconConverter Instance { get; } = new ConnectivityIconConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isOffline)
            {
                // Return Material Icons code: offline_bolt for offline, wifi for online
                return isOffline ? "\ue932" : "\ue63e";
            }

            return "\ue63e"; // Default to online/wifi icon
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
