using System.Runtime.ExceptionServices;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Starfield;

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
    /// <exception cref="PluginOverlayReadException">Mutagen or the filesystem reports unreadable Plugin data.</exception>
    public IModDisposeGetter ReadOverlay(
        string pluginPath,
        GameRelease gameRelease,
        BinaryReadParameters readParameters)
    {
        try
        {
            return gameRelease switch
            {
                GameRelease.Oblivion => OblivionMod.CreateFromBinaryOverlay(pluginPath,
                    OblivionRelease.Oblivion,
                    readParameters),
                GameRelease.SkyrimLE => SkyrimMod.CreateFromBinaryOverlay(pluginPath,
                    SkyrimRelease.SkyrimLE,
                    readParameters),
                GameRelease.SkyrimSE => SkyrimMod.CreateFromBinaryOverlay(pluginPath,
                    SkyrimRelease.SkyrimSE,
                    readParameters),
                GameRelease.SkyrimSEGog => SkyrimMod.CreateFromBinaryOverlay(pluginPath,
                    SkyrimRelease.SkyrimSEGog,
                    readParameters),
                GameRelease.SkyrimVR => SkyrimMod.CreateFromBinaryOverlay(pluginPath,
                    SkyrimRelease.SkyrimVR,
                    readParameters),
                GameRelease.EnderalLE => SkyrimMod.CreateFromBinaryOverlay(pluginPath,
                    SkyrimRelease.EnderalLE,
                    readParameters),
                GameRelease.EnderalSE => SkyrimMod.CreateFromBinaryOverlay(pluginPath,
                    SkyrimRelease.EnderalSE,
                    readParameters),
                GameRelease.Fallout4 => Fallout4Mod.CreateFromBinaryOverlay(pluginPath,
                    Fallout4Release.Fallout4,
                    readParameters),
                GameRelease.Fallout4VR => Fallout4Mod.CreateFromBinaryOverlay(pluginPath,
                    Fallout4Release.Fallout4VR,
                    readParameters),
                GameRelease.Starfield => StarfieldMod.CreateFromBinaryOverlay(pluginPath,
                    StarfieldRelease.Starfield,
                    readParameters),
                _ => throw new NotSupportedException($"Unsupported game release: {gameRelease}")
            };
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
