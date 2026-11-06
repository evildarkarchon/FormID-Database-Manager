#nullable enable

using FormID_Database_Manager.Services;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

/// <summary>
///     Covers the captured Processing Run inputs' own guard: a capture that could not have come from one read of the
///     Game Context is a programming error and fails on construction, rather than reaching Processing Run Admission as a
///     misleading refusal or a run against the wrong game (ADR-0010 2026-11-05 amendment).
/// </summary>
public sealed class ProcessingRunInputsTests
{
    /// <summary>
    ///     A game directory that is never created: building a Plugin List Source from it is pure path arithmetic.
    /// </summary>
    private readonly string _gameDirectory =
        Path.Combine(Path.GetTempPath(), $"FormIdTests-Inputs-{Guid.NewGuid():N}");

    /// <summary>
    ///     Which Plugin List confirmation a case captures.
    /// </summary>
    /// <remarks>Public because public theory methods take it; the confirmation types themselves are internal.</remarks>
    public enum Capture
    {
        /// <summary>No confirmation: the Game Context holds no Plugin List Source.</summary>
        None,

        /// <summary>The loading singleton.</summary>
        Loading,

        /// <summary>The unavailable singleton.</summary>
        Unavailable,

        /// <summary>A SkyrimSE Confirmed Plugin List wrapped in the available case.</summary>
        Available
    }

    /// <summary>
    ///     Verifies that any confirmation captured without a GameRelease is rejected: a confirmation exists only for a
    ///     Plugin List Source, and the Game Context holds a source only beside a GameRelease (ADR-0011).
    /// </summary>
    /// <param name="capture">The confirmation captured without a GameRelease.</param>
    [Theory]
    [InlineData(Capture.Loading)]
    [InlineData(Capture.Unavailable)]
    [InlineData(Capture.Available)]
    public void Constructor_ConfirmationWithoutGameRelease_ThrowsArgumentException(Capture capture)
    {
        var confirmation = Confirmation(capture);

        Assert.Throws<ArgumentException>(() => Inputs(null, confirmation));
    }

    /// <summary>
    ///     Verifies that a matching Confirmed Plugin List for a GameRelease other than the captured one is rejected,
    ///     because the GameRelease and the source the confirmation was asked about come from one Game Context state.
    /// </summary>
    [Fact]
    public void Constructor_AvailableConfirmationForAnotherGameRelease_ThrowsArgumentException()
    {
        var confirmation = Confirmation(Capture.Available);

        Assert.Throws<ArgumentException>(() => Inputs(GameRelease.Fallout4, confirmation));
    }

    /// <summary>
    ///     Verifies that every capture one read of the Game Context can produce constructs and keeps what it was given,
    ///     including no confirmation with or without a GameRelease, and the loading or unavailable answer for a release.
    /// </summary>
    /// <param name="hasGameRelease">Whether SkyrimSE is captured as the GameRelease.</param>
    /// <param name="capture">The confirmation captured with it.</param>
    [Theory]
    [InlineData(false, Capture.None)]
    [InlineData(true, Capture.None)]
    [InlineData(true, Capture.Loading)]
    [InlineData(true, Capture.Unavailable)]
    [InlineData(true, Capture.Available)]
    public void Constructor_ConsistentCapture_KeepsTheCapturedValues(bool hasGameRelease, Capture capture)
    {
        GameRelease? gameRelease = hasGameRelease ? GameRelease.SkyrimSE : null;
        var confirmation = Confirmation(capture);

        var inputs = new ProcessingRunInputs(
            gameRelease,
            confirmation,
            @"C:\Lists\formids.txt",
            @"C:\Databases\formids.db",
            UpdateMode.ReplacePluginRecords,
            true);

        Assert.Equal(gameRelease, inputs.GameRelease);
        Assert.Same(confirmation, inputs.PluginListConfirmation);
        Assert.Equal(@"C:\Lists\formids.txt", inputs.FormIdListPath);
        Assert.Equal(@"C:\Databases\formids.db", inputs.DatabasePath);
        Assert.Equal(UpdateMode.ReplacePluginRecords, inputs.UpdateMode);
        Assert.True(inputs.DryRun);
    }

    /// <summary>
    ///     Builds the confirmation a case captures; the available case confirms SkyrimSE in the test's game directory.
    /// </summary>
    private PluginListConfirmation? Confirmation(Capture capture)
    {
        return capture switch
        {
            Capture.None => null,
            Capture.Loading => ConfirmedPluginListLoading.Instance,
            Capture.Unavailable => ConfirmedPluginListUnavailable.Instance,
            Capture.Available => new ConfirmedPluginListAvailable(new ConfirmedPluginList(
                1,
                PluginListSource.Create(GameRelease.SkyrimSE, _gameDirectory),
                AdvancedMode.Off,
                [new PluginListEntry("User.esp")],
                ["User.esp"])),
            _ => throw new ArgumentOutOfRangeException(nameof(capture), capture, "Unsupported capture.")
        };
    }

    /// <summary>
    ///     Constructs captured inputs for a selected-Plugin run with the given GameRelease and confirmation.
    /// </summary>
    private static ProcessingRunInputs Inputs(GameRelease? gameRelease, PluginListConfirmation? confirmation)
    {
        return new ProcessingRunInputs(
            gameRelease,
            confirmation,
            null,
            @"C:\Databases\formids.db",
            UpdateMode.Append,
            false);
    }
}
