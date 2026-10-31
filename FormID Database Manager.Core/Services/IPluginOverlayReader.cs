using System.Runtime.ExceptionServices;
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
    ///     failure, so it deliberately does not arrive as <see cref="PluginOverlayReadException" />.
    /// </exception>
    /// <exception cref="PluginOverlayReadException">The selected Plugin contains malformed or unreadable data.</exception>
    /// <exception cref="UnresolvableMasterException">
    ///     The prepared read capability cannot resolve a master the selected Plugin declares (ADR-0006).
    /// </exception>
    IModDisposeGetter ReadOverlay(SelectedPluginReady readyPlugin);
}

/// <summary>
///     Marks an expected Plugin-specific failure raised by the binary-overlay adapter.
/// </summary>
/// <param name="message">The underlying Plugin-read message.</param>
/// <param name="innerException">The Mutagen or filesystem exception.</param>
internal sealed class PluginOverlayReadException(string message, Exception innerException)
    : Exception(message, innerException);

internal sealed class MutagenPluginOverlayReader : IPluginOverlayReader
{
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
    /// <exception cref="PluginOverlayReadException">Mutagen or the filesystem reports unreadable Plugin data.</exception>
    /// <exception cref="UnresolvableMasterException">
    ///     Mutagen raises <see cref="MissingModException" /> (naming the master's file name) or
    ///     <see cref="MissingModMappingException" /> (naming no master); the Mutagen exception is the inner exception.
    /// </exception>
    public IModDisposeGetter ReadOverlay(SelectedPluginReady readyPlugin)
    {
        ArgumentNullException.ThrowIfNull(readyPlugin);

        // Validate adapter ownership before delegating to any Supported GameRelease factory or filesystem work.
        var payload = readyPlugin.ReadCapability.RequirePayload<MutagenPluginReadCapabilityPayload>();
        // Resolved outside the try: an unsupported GameRelease is a programming error, and the ArgumentOutOfRangeException
        // it raises would otherwise be normalized into a Failed Plugin by the expected-failure check below, which
        // deliberately treats ArgumentException as a malformed-Plugin signal from Mutagen.
        var createOverlay = SupportedGameReleases.ForRelease(payload.GameRelease).CreateOverlay;

        try
        {
            return createOverlay(readyPlugin.ResolvedPluginPath, payload.ReadParameters);
        }
        catch (Exception ex)
        {
            RethrowNestedCancellation(ex);
            if (IsExpectedPluginOverlayFailure(ex))
            {
                // The application-owned marker prevents arbitrary adapter failures from becoming Failed Plugins.
                throw new PluginOverlayReadException(GetUnderlyingPluginReadMessage(ex), ex);
            }

            // Classified after the Failed Plugin list, as Plugin Ingestion used to classify whatever escaped it, so a
            // master-resolution failure wrapping a listed cause still becomes the same Failed Plugin it did before.
            ThrowIfUnresolvableMaster(readyPlugin.PluginName, ex);
            throw;
        }
    }

    /// <summary>
    ///     Classifies Mutagen's two master-resolution failures as an Unresolvable Master that fails the Processing Run.
    /// </summary>
    /// <param name="pluginName">The selected Plugin whose declared master could not be resolved.</param>
    /// <param name="exception">The top-level exception raised while opening the overlay.</param>
    /// <remarks>
    ///     This is a classification of its own beside the Failed Plugin list, not an entry in it. The list stays narrow
    ///     so an unexpected internal failure still aborts loudly, and these failures are not Plugin-specific anyway:
    ///     they are facts about the Data directory that would fail every selected Plugin identically (ADR-0006,
    ///     ADR-0015, issue #52). Only the top-level exception is inspected, matching the catch Plugin Ingestion held.
    /// </remarks>
    /// <exception cref="UnresolvableMasterException">
    ///     <paramref name="exception" /> is a <see cref="MissingModException" /> or
    ///     <see cref="MissingModMappingException" />, which is kept as the inner exception.
    /// </exception>
    private static void ThrowIfUnresolvableMaster(string pluginName, Exception exception)
    {
        switch (exception)
        {
            case MissingModException missingMod:
                // ModPath is the first of the exception's keys, which is the only one here: Mutagen's separated-master
                // path raises this per unresolved master, from a ModKey rather than a path, so it carries exactly one
                // and that key has no directory to report — only its file name.
                throw new UnresolvableMasterException(
                    pluginName,
                    missingMod.ModPath.ModKey.FileName.ToString(),
                    missingMod);
            case MissingModMappingException missingMapping:
                // Mutagen reports only that no lookup was supplied, so there is no individual master to name.
                throw new UnresolvableMasterException(pluginName, null, missingMapping);
        }
    }

    /// <summary>
    ///     Identifies the concrete Mutagen and filesystem failures that mean the selected Plugin could not be opened.
    /// </summary>
    /// <param name="exception">The exception raised by Mutagen while opening the configured Plugin path.</param>
    /// <returns><see langword="true" /> only for expected malformed or unreadable Plugin failures.</returns>
    private static bool IsExpectedPluginOverlayFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is MalformedDataException
                or RecordException
                or ArgumentException
                or OverflowException
                or IOException
                or UnauthorizedAccessException)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Retains the deepest underlying Plugin-read message when Mutagen enriches an opening failure.
    /// </summary>
    /// <param name="exception">The adapter exception chain.</param>
    /// <returns>The deepest underlying exception message.</returns>
    private static string GetUnderlyingPluginReadMessage(Exception exception)
    {
        var underlying = exception;
        while (underlying.InnerException is { } innerException)
        {
            underlying = innerException;
        }

        return underlying.Message;
    }

    /// <summary>
    ///     Preserves cancellation before any expected Plugin-opening failure is normalized.
    /// </summary>
    /// <param name="exception">The possible adapter wrapper.</param>
    /// <exception cref="OperationCanceledException">The exception chain contains cancellation.</exception>
    private static void RethrowNestedCancellation(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is OperationCanceledException cancellation)
            {
                ExceptionDispatchInfo.Capture(cancellation).Throw();
            }
        }
    }
}
