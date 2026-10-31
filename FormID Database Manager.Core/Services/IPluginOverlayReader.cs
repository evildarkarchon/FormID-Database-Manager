using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Records;

namespace FormID_Database_Manager.Services;

internal interface IPluginOverlayReader
{
    /// <summary>
    ///     Opens one ready selected Plugin through the configured binary-overlay implementation.
    /// </summary>
    /// <param name="readyPlugin">The prepared selected Plugin and its opaque read capability.</param>
    /// <returns>The disposable Plugin overlay.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="readyPlugin" /> is null.</exception>
    /// <exception cref="InvalidOperationException">The read capability belongs to an incompatible adapter.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     The capability identifies an unsupported GameRelease. This is a programming error, not a Plugin-read
    ///     failure, so it deliberately does not arrive as <see cref="PluginReadException" />.
    /// </exception>
    /// <exception cref="PluginReadException">
    ///     The selected Plugin contains malformed or unreadable data; the phase is
    ///     <see cref="PluginReadPhase.OpeningPlugin" />.
    /// </exception>
    /// <exception cref="UnresolvableMasterException">
    ///     The prepared read capability cannot resolve a master the selected Plugin declares (ADR-0006).
    /// </exception>
    IModDisposeGetter ReadOverlay(SelectedPluginReady readyPlugin);
}

/// <summary>
///     The overlay seam's production adapter, kept until Plugin Ingestion reads through <see cref="IPluginReader" />.
/// </summary>
/// <remarks>
///     It owns no classification rule: every opening rule lives in <see cref="MutagenPluginReader" />, and this
///     adapter delegates to it so no rule exists in two places while both seams do (ADR-0015).
/// </remarks>
internal sealed class MutagenPluginOverlayReader : IPluginOverlayReader
{
    private readonly MutagenPluginReader _pluginReader = new();

    /// <summary>
    ///     Opens one ready selected Plugin using the production read state hidden in its opaque capability.
    /// </summary>
    /// <param name="readyPlugin">The selected Plugin path and capability prepared by production Game Load Orders.</param>
    /// <returns>The disposable Mutagen overlay for the capability's Supported GameRelease.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="readyPlugin" /> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     The capability identifies a GameRelease this application does not support.
    /// </exception>
    /// <exception cref="InvalidOperationException">The capability belongs to an incompatible adapter.</exception>
    /// <exception cref="PluginReadException">
    ///     Mutagen or the filesystem reports unreadable Plugin data; the phase is
    ///     <see cref="PluginReadPhase.OpeningPlugin" /> and the message is the deepest underlying one.
    /// </exception>
    /// <exception cref="UnresolvableMasterException">
    ///     Mutagen raises <see cref="MissingModException" /> (naming the master's file name) or
    ///     <see cref="MissingModMappingException" /> (naming no master); the Mutagen exception is the inner exception.
    /// </exception>
    public IModDisposeGetter ReadOverlay(SelectedPluginReady readyPlugin)
    {
        return _pluginReader.OpenOverlay(readyPlugin);
    }
}
