#nullable enable

using FormID_Database_Manager.Services;

namespace FormID_Database_Manager.Tests.Fakes;

/// <summary>
///     Which Plugin List confirmation a test captures with a press of Process FormIDs.
/// </summary>
/// <remarks>Public because public theory methods take it; the confirmation types themselves are internal.</remarks>
public enum PluginListConfirmationCapture
{
    /// <summary>No confirmation: the Game Context holds no Plugin List Source.</summary>
    None,

    /// <summary>The Plugin List is still loading a Confirmed Plugin List for the Game Context's source.</summary>
    Loading,

    /// <summary>The Plugin List cannot supply a Confirmed Plugin List for the Game Context's source.</summary>
    Unavailable,

    /// <summary>A matching Confirmed Plugin List that selects no Plugins.</summary>
    AvailableWithoutSelection,

    /// <summary>A matching Confirmed Plugin List that selects one Plugin.</summary>
    AvailableWithSelection
}

/// <summary>
///     Builds Plugin List confirmations and Confirmed Plugin Lists as plain values, as the Plugin List would answer them,
///     so tests of the captured inputs, admission, and Processing Run Control construct no live Plugin List.
/// </summary>
/// <remarks>
///     How each Plugin List state answers its confirmation is covered by <c>PluginListTests</c>; these values only stand
///     in for that answer (ADR-0009, ADR-0010 2026-11-05 amendment).
/// </remarks>
internal static class PluginListConfirmations
{
    /// <summary>The one Plugin the available captures list.</summary>
    internal const string PluginName = "User.esp";

    /// <summary>
    ///     Builds the confirmation a case captures for the given source.
    /// </summary>
    /// <param name="capture">Which confirmation to build.</param>
    /// <param name="source">
    ///     The Plugin List Source an available confirmation was confirmed against; ignored by the other cases.
    /// </param>
    /// <returns>The confirmation, or null for <see cref="PluginListConfirmationCapture.None" />.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capture" /> is not a declared case.</exception>
    internal static PluginListConfirmation? For(PluginListConfirmationCapture capture, PluginListSource source)
    {
        return capture switch
        {
            PluginListConfirmationCapture.None => null,
            PluginListConfirmationCapture.Loading => ConfirmedPluginListLoading.Instance,
            PluginListConfirmationCapture.Unavailable => ConfirmedPluginListUnavailable.Instance,
            PluginListConfirmationCapture.AvailableWithoutSelection =>
                Available(source, [PluginName], []),
            PluginListConfirmationCapture.AvailableWithSelection =>
                Available(source, [PluginName], [PluginName]),
            _ => throw new ArgumentOutOfRangeException(nameof(capture), capture, "Unsupported capture.")
        };
    }

    /// <summary>
    ///     Builds the available confirmation wrapping a Confirmed Plugin List for the given source.
    /// </summary>
    /// <param name="source">The Plugin List Source the list was confirmed against.</param>
    /// <param name="pluginNames">Every Plugin in Plugin List order.</param>
    /// <param name="selectedPluginNames">The selected Plugins, already in Plugin List order.</param>
    internal static ConfirmedPluginListAvailable Available(
        PluginListSource source,
        string[] pluginNames,
        string[] selectedPluginNames)
    {
        return new ConfirmedPluginListAvailable(ConfirmedList(source, pluginNames, selectedPluginNames));
    }

    /// <summary>
    ///     Builds a Confirmed Plugin List as a plain value, as the Plugin List would hold it for the given source.
    /// </summary>
    /// <param name="source">The Plugin List Source the list was confirmed against.</param>
    /// <param name="pluginNames">Every Plugin in Plugin List order.</param>
    /// <param name="selectedPluginNames">The selected Plugins, already in Plugin List order.</param>
    internal static ConfirmedPluginList ConfirmedList(
        PluginListSource source,
        string[] pluginNames,
        string[] selectedPluginNames)
    {
        return new ConfirmedPluginList(
            1,
            source,
            AdvancedMode.Off,
            [.. pluginNames.Select(static name => new PluginListEntry(name))],
            [.. selectedPluginNames]);
    }
}
