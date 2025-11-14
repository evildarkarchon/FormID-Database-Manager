using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace FormID_Database_Manager;

/// <summary>
///     A value converter for converting integer input values to boolean based on specific conditions.
/// </summary>
/// <remarks>
///     This converter is generally used in data-binding scenarios in Avalonia UI applications.
///     It evaluates an integer value and returns true if the integer is greater than zero, otherwise false.
/// </remarks>
public class BooleanConverter : IValueConverter
{
    /// <summary>
    ///     Converts an integer value to a boolean indicating whether the integer is greater than zero.
    /// </summary>
    /// <param name="value">The input value to convert. Expected to be an integer.</param>
    /// <param name="targetType">The type of the binding target property. Not used in this implementation.</param>
    /// <param name="parameter">Optional parameter for the converter. Not used in this implementation.</param>
    /// <param name="culture">The culture information for the conversion. Not used in this implementation.</param>
    /// <returns>Returns true if the provided integer is greater than zero; otherwise, returns false.</returns>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int count)
        {
            return count > 0;
        }

        return false;
    }

    /// <summary>
    ///     Converts a boolean value back to its original integer representation.
    /// </summary>
    /// <param name="value">The boolean value to convert back. Expected to be a boolean.</param>
    /// <param name="targetType">The type of the binding target property. Not used in this implementation.</param>
    /// <param name="parameter">Optional parameter for the converter. Not used in this implementation.</param>
    /// <param name="culture">The culture information for the conversion. Not used in this implementation.</param>
    /// <returns>Throws a NotImplementedException as this conversion is not currently implemented.</returns>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
