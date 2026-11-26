using System;
using System.Globalization;
using Avalonia.Data.Converters;
using DeveloperTools.Localization;

namespace DeveloperTools.Converters;

/// <summary>Shows a stored English value (e.g. "System", "Dark", "Preview") in the current language.</summary>
public sealed class Translate : IValueConverter
{
    public static readonly Translate Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? null : Loc.T(value.ToString() ?? "");

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
