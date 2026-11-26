using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace DeveloperTools.Converters;

/// <summary>Turns an icon resource key ("Icon.Json") into its geometry.</summary>
public sealed class ResourceGeometry : IValueConverter
{
    public static readonly ResourceGeometry Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string key && Application.Current?.TryFindResource(key, out var resource) == true ? resource as Geometry : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
