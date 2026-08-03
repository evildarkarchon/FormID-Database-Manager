using System;
using Microsoft.UI.Xaml.Data;

namespace FormID_Database_Manager.WinUI.Converters;

/// <summary>
/// Maps a boolean binding source to a <see cref="Visibility"/> so a collapsed control gives up its layout space.
/// </summary>
/// <remarks>
/// This lives in the WinUI project rather than the core project because <see cref="Visibility"/> is a platform type,
/// and the core-boundary assertion requires core to stay free of any platform reference.
/// </remarks>
public sealed class BooleanToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// Converts a boolean source value to a visibility.
    /// </summary>
    /// <param name="value">The source value; anything that is not a <see cref="bool"/> is treated as false.</param>
    /// <param name="targetType">The binding target type. Unused: this converter only ever produces a visibility.</param>
    /// <param name="parameter">The converter parameter. Unused.</param>
    /// <param name="language">The binding language. Unused: the mapping is not culture-sensitive.</param>
    /// <returns><see cref="Visibility.Visible"/> when the source is true, otherwise <see cref="Visibility.Collapsed"/>.</returns>
    public object Convert(object? value, Type? targetType, object? parameter, string? language)
    {
        // A null or non-boolean source means "no fact to show", which is the same presentation as false. Throwing
        // here would take the window down for a binding glitch that costs nothing to absorb.
        return value is true ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Not supported: every binding through this converter is one-way from the ViewModel.
    /// </summary>
    /// <param name="value">The target value. Unused.</param>
    /// <param name="targetType">The source type. Unused.</param>
    /// <param name="parameter">The converter parameter. Unused.</param>
    /// <param name="language">The binding language. Unused.</param>
    /// <returns>This method never returns.</returns>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public object ConvertBack(object? value, Type? targetType, object? parameter, string? language)
    {
        throw new NotSupportedException(
            "BooleanToVisibilityConverter is one-way; visibility is never pushed back to the ViewModel.");
    }
}
