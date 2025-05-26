using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RenewitSalesforceApp.Converters
{
    public class ConnectivityColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isOffline)
            {
                return isOffline ? Color.FromHex("#F44336") : Color.FromHex("#4CAF50");
            }
            return Color.FromHex("#4CAF50");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
