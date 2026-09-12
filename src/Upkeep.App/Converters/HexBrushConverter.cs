using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Upkeep.App.Converters;

/// <summary>
/// Turns a "#RRGGBB" string into a brush.
/// <para>
/// It exists so the treemap's tints can be chosen in the view model — which is where the size
/// banding is decided — without the view model referencing a WinUI type. A computed Brush property
/// on a view model looks tidy right up until a plain unit test constructs it with no
/// Application.Current and the whole class stops being testable.
/// </para>
/// </summary>
public sealed partial class HexBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string hex || hex.Length is not (7 or 9) || hex[0] != '#')
        {
            return new SolidColorBrush(Colors.Transparent);
        }

        try
        {
            int offset = hex.Length == 9 ? 3 : 1;
            byte alpha = hex.Length == 9 ? byte.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) : (byte)255;
            byte red = byte.Parse(hex.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte green = byte.Parse(hex.AsSpan(offset + 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte blue = byte.Parse(hex.AsSpan(offset + 4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

            return new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
        }
        catch (FormatException)
        {
            return new SolidColorBrush(Colors.Transparent);
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("Brushes are never converted back to hex.");
}
