using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace DeveloperTools.Converters;

/// <summary>
/// Binds a RadioButton to an enum value: IsChecked="{Binding Mode, Converter={x:Static c:EnumEquals.Instance}, ConverterParameter=Json}".
/// </summary>
public sealed class EnumEquals : IValueConverter
{
    public static readonly EnumEquals Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.ToString() == parameter as string;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is string name ? Enum.Parse(Nullable.GetUnderlyingType(targetType) ?? targetType, name) : BindingOperations.DoNothing;
}
