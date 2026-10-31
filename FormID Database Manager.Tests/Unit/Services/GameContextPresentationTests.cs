#nullable enable

using System;
using FormID_Database_Manager.Services;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

/// <summary>
///     Covers every word Game Context shows the user, and which message list each notice belongs to.
/// </summary>
/// <remarks>
///     Issue #115, under parent #114 (ADR-0013). This is the only place Game Context wording is asserted: Game Context
///     reports typed notices, and its own tests compare against what this renderer returns rather than restating a
///     sentence. Each case constructs its notice directly, so this suite cannot fail for Game Context's reasons.
/// </remarks>
public sealed class GameContextPresentationTests
{
    /// <summary>
    ///     Verifies the error a failed game-directory picker reaches the user as, with the picker's own error text.
    /// </summary>
    [Fact]
    public void Render_GameDirectoryPickerFailed_ReturnsThePickerErrorAsAnError()
    {
        var rendered = GameContextPresentation.Render(new GameDirectoryPickerFailed("picker unavailable"));

        Assert.Equal(MessageList.Error, rendered.List);
        Assert.Equal("Error selecting game directory: picker unavailable", rendered.Text);
    }

    /// <summary>
    ///     Verifies that a GameRelease with no usable installed location is information, not an error, and points the
    ///     user at Browse.
    /// </summary>
    [Fact]
    public void Render_NoInstalledLocationsFound_ReturnsTheBrowseGuidanceAsInformation()
    {
        var rendered = GameContextPresentation.Render(new NoInstalledLocationsFound(GameRelease.Fallout4));

        Assert.Equal(MessageList.Information, rendered.List);
        Assert.Equal("No installed locations found for Fallout4. Use Browse to select a directory.", rendered.Text);
    }

    /// <summary>
    ///     Verifies the one error every refused directory shares, carrying the failure's message (ADR-0011).
    /// </summary>
    [Fact]
    public void Render_DirectoryRefused_ReturnsTheUnreadableDirectoryErrorWithTheFailureMessage()
    {
        var rendered = GameContextPresentation.Render(new DirectoryRefused("drive not ready"));

        Assert.Equal(MessageList.Error, rendered.List);
        Assert.Equal("Could not read the selected directory: drive not ready", rendered.Text);
    }

    /// <summary>
    ///     Verifies the error a directory with no detectable game reaches the user as, which asks for a GameRelease.
    /// </summary>
    [Fact]
    public void Render_NoGameDetected_ReturnsTheDropdownGuidanceAsAnError()
    {
        var rendered = GameContextPresentation.Render(new NoGameDetected());

        Assert.Equal(MessageList.Error, rendered.List);
        Assert.Equal(
            "Could not detect game from directory. Please select a game from the dropdown.",
            rendered.Text);
    }

    /// <summary>
    ///     Verifies that a missing notice is a programming error rather than an empty message.
    /// </summary>
    [Fact]
    public void Render_NullNotice_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => GameContextPresentation.Render(null!));
    }

    /// <summary>
    ///     Verifies that a notice this module does not know is refused rather than rendered as empty text, so adding a
    ///     notice without wording fails loudly instead of showing the user nothing.
    /// </summary>
    [Fact]
    public void Render_UnknownNotice_ThrowsArgumentOutOfRangeException()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => GameContextPresentation.Render(new UnknownNotice()));

        Assert.Equal("notice", exception.ParamName);
    }

    /// <summary>
    ///     A notice outside the closed set Presentation renders. The base record's implicit constructor is protected,
    ///     so a test can derive one without Core exposing anything for it.
    /// </summary>
    private sealed record UnknownNotice : GameContextNotice;
}
