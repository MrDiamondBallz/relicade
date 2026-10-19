using System.Globalization;
using Avalonia.Data.Converters;

namespace Relicade;

/// <summary>Only show a missing-art label after loading finishes without a decoded image.</summary>
public sealed class ArtworkUnavailableConverter : IMultiValueConverter
{
    public static readonly ArtworkUnavailableConverter Instance = new();

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Count == 2 && values[0] is false && values[1] is null;
}
