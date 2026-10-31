#nullable enable

using System.Collections.Immutable;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.TestUtilities.Mocks;
using FormID_Database_Manager.ViewModels;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

/// <summary>
///     Covers the Game Context sink adapter: what each report it receives leaves in the ViewModel (ADR-0013, ADR-0014).
/// </summary>
/// <remarks>
///     Issue #120, under parent #118. The adapter is called directly over a real ViewModel with
///     <see cref="SynchronousThreadDispatcher" />, so no Game Context, Plugin List or picker is involved. When Game
///     Context reports is pinned by <see cref="GameContextTests" />, and the wording and list of each notice by
///     <see cref="GameContextPresentationTests" />; the expected text here always comes from Presentation's output.
///     The assertions read ViewModel state rather than which ViewModel members were called, so they hold however the
///     adapter writes.
/// </remarks>
public sealed class ViewModelGameContextSinkTests
{
    private const string GameDirectory = @"C:\Games\Skyrim";
    private const string SecondDirectory = @"D:\Games\Skyrim";

    private readonly ViewModelGameContextSink _sut;
    private readonly MainWindowViewModel _viewModel = new(new SynchronousThreadDispatcher());

    public ViewModelGameContextSinkTests()
    {
        _sut = new ViewModelGameContextSink(_viewModel);
    }

    /// <summary>
    ///     Verifies that every shown field of a reported Game Context state reaches the ViewModel's projection, and that
    ///     a later state clearing the selection replaces it whole.
    /// </summary>
    /// <remarks>
    ///     Advanced Mode is on in both states so all four projected fields differ from the ViewModel's defaults, and so
    ///     the cleared state shows that clearing the selection does not reset the mode.
    /// </remarks>
    [Fact]
    public void ReportState_CompleteThenClearedState_ReplacesTheViewModelProjectionWhole()
    {
        _sut.ReportState(new GameContextState(
            GameRelease.SkyrimSE,
            GameDirectory,
            PluginListSource.Create(GameRelease.SkyrimSE, GameDirectory),
            [GameDirectory, SecondDirectory],
            AdvancedMode.On));

        Assert.Equal(GameRelease.SkyrimSE, _viewModel.SelectedGame);
        Assert.Equal(GameDirectory, _viewModel.GameDirectory);
        Assert.Equal([GameDirectory, SecondDirectory], _viewModel.DetectedDirectories);
        Assert.True(_viewModel.AdvancedMode);

        _sut.ReportState(new GameContextState(null, null, null, ImmutableArray<string>.Empty, AdvancedMode.On));

        Assert.Null(_viewModel.SelectedGame);
        Assert.Empty(_viewModel.GameDirectory);
        Assert.Empty(_viewModel.DetectedDirectories);
        Assert.True(_viewModel.AdvancedMode);
    }

    /// <summary>
    ///     Verifies that a picker-failure notice reaches the list Game Context Presentation chooses, with the text it
    ///     renders.
    /// </summary>
    [Fact]
    public void ReportNotice_GameDirectoryPickerFailed_AddsTheRenderedTextToItsList()
    {
        AssertReportedNoticeReachesOnlyItsList(new GameDirectoryPickerFailed("picker unavailable"));
    }

    /// <summary>
    ///     Verifies that a no-installed-locations notice reaches the list Game Context Presentation chooses, with the
    ///     text it renders.
    /// </summary>
    [Fact]
    public void ReportNotice_NoInstalledLocationsFound_AddsTheRenderedTextToItsList()
    {
        AssertReportedNoticeReachesOnlyItsList(new NoInstalledLocationsFound(GameRelease.Fallout4));
    }

    /// <summary>
    ///     Verifies that a refused-directory notice reaches the list Game Context Presentation chooses, with the text it
    ///     renders.
    /// </summary>
    [Fact]
    public void ReportNotice_DirectoryRefused_AddsTheRenderedTextToItsList()
    {
        AssertReportedNoticeReachesOnlyItsList(new DirectoryRefused("drive not ready"));
    }

    /// <summary>
    ///     Verifies that a no-game-detected notice reaches the list Game Context Presentation chooses, with the text it
    ///     renders.
    /// </summary>
    [Fact]
    public void ReportNotice_NoGameDetected_AddsTheRenderedTextToItsList()
    {
        AssertReportedNoticeReachesOnlyItsList(new NoGameDetected());
    }

    /// <summary>
    ///     Reports one notice to the adapter and asserts that exactly the text Game Context Presentation renders for it
    ///     landed in the list Presentation chose, with every other Message List left empty.
    /// </summary>
    /// <param name="notice">The notice to report.</param>
    /// <remarks>The expected text and list come from Presentation, so no sentence is restated here.</remarks>
    private void AssertReportedNoticeReachesOnlyItsList(GameContextNotice notice)
    {
        var rendered = GameContextPresentation.Render(notice);

        _sut.ReportNotice(notice);

        string[] expectedErrors = rendered.List == GameContextMessageList.Error ? [rendered.Text] : [];
        string[] expectedInformation = rendered.List == GameContextMessageList.Information ? [rendered.Text] : [];
        Assert.Equal(expectedErrors, _viewModel.ErrorMessages);
        Assert.Equal(expectedInformation, _viewModel.InformationMessages);
        Assert.Empty(_viewModel.WarningMessages);
    }
}
