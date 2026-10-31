using FormID_Database_Manager.ViewModels;

namespace FormID_Database_Manager.Services;

/// <summary>
///     The User Workflow's adapter on the Game Context sink: it performs every ViewModel write the Game Context
///     reports (ADR-0013).
/// </summary>
/// <param name="viewModel">The ViewModel the reports are written to.</param>
/// <remarks>
///     <para>
///         Game Context reports from the UI thread and holds no lock, so these writes need none either. It is a
///         separate class from <see cref="ViewModelProcessingRunControlSink" /> because the two sinks have different
///         ordering contracts: that one is called under Processing Run Control's lock, this one is UI-thread-affine.
///     </para>
///     <para>
///         A write may raise a notification that re-enters a Game Context intent, such as a ComboBox echoing the
///         projected selection. Game Context has already written the state it reports, so the echo reads it.
///     </para>
///     <para>
///         A module of its own that <see cref="UserWorkflow" /> constructs and hands to the Game Context it composes,
///         so its writes can be tested without the rest of the workflow (ADR-0014).
///     </para>
/// </remarks>
internal sealed class ViewModelGameContextSink(MainWindowViewModel viewModel) : IGameContextSink
{
    /// <inheritdoc />
    /// <remarks>
    ///     One projection call carries all four shown fields, so observers never see fields from two states. The
    ///     Plugin List Source is not shown, and the ViewModel's own dispatch handles a call from off the UI thread.
    /// </remarks>
    public void ReportState(GameContextState state)
    {
        viewModel.ApplyGameContextProjection(
            state.SelectedGameRelease,
            state.SelectedGameDirectory,
            state.AvailableDirectories,
            state.AdvancedMode);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Which list a notice belongs to is Presentation's choice, so it is passed straight through with no per-list
    ///     switch here (ADR-0014).
    /// </remarks>
    public void ReportNotice(GameContextNotice notice)
    {
        var rendered = GameContextPresentation.Render(notice);
        viewModel.AddMessage(rendered.List, rendered.Text);
    }
}
