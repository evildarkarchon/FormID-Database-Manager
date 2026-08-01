using System.Runtime.ExceptionServices;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Records;

namespace FormID_Database_Manager.Services;

internal interface IPluginOverlayReader
{
    /// <summary>
    ///     Opens one Plugin through the configured binary-overlay implementation.
    /// </summary>
    /// <exception cref="PluginOverlayReadException">The selected Plugin contains malformed or unreadable data.</exception>
    IModDisposeGetter ReadOverlay(
        string pluginPath,
        GameRelease gameRelease,
        BinaryReadParameters readParameters);
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
    ///     Opens a Mutagen overlay and normalizes only known malformed or unreadable Plugin failures.
    /// </summary>
    /// <param name="pluginPath">The available selected Plugin path.</param>
    /// <param name="gameRelease">The target GameRelease.</param>
    /// <param name="readParameters">The shared load-order-aware binary read parameters.</param>
    /// <returns>The disposable Mutagen overlay.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="gameRelease" /> is not a Supported GameRelease.</exception>
    /// <exception cref="PluginOverlayReadException">Mutagen or the filesystem reports unreadable Plugin data.</exception>
    public IModDisposeGetter ReadOverlay(
        string pluginPath,
        GameRelease gameRelease,
        BinaryReadParameters readParameters)
    {
        // Resolved outside the try: an unsupported GameRelease is a programming error, and the ArgumentOutOfRangeException
        // it raises would otherwise be normalized into a Failed Plugin by the expected-failure check below, which
        // deliberately treats ArgumentException as a malformed-Plugin signal from Mutagen.
        var createOverlay = SupportedGameReleases.ForRelease(gameRelease).CreateOverlay;

        try
        {
            return createOverlay(pluginPath, readParameters);
        }
        catch (Exception ex)
        {
            RethrowNestedCancellation(ex);
            if (!IsExpectedPluginOverlayFailure(ex))
            {
                throw;
            }

            // The application-owned marker prevents arbitrary adapter failures from becoming Failed Plugins.
            throw new PluginOverlayReadException(GetUnderlyingPluginReadMessage(ex), ex);
        }
    }

    /// <summary>
    ///     Identifies the concrete Mutagen and filesystem failures that mean the selected Plugin could not be opened.
    /// </summary>
    /// <param name="exception">The exception raised by Mutagen while opening the configured Plugin path.</param>
    /// <returns><see langword="true" /> only for expected malformed or unreadable Plugin failures.</returns>
    private static bool IsExpectedPluginOverlayFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
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
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is OperationCanceledException cancellation)
            {
                ExceptionDispatchInfo.Capture(cancellation).Throw();
            }
        }
    }
}
