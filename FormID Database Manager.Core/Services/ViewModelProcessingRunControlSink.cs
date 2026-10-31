using FormID_Database_Manager.ViewModels;

namespace FormID_Database_Manager.Services;

/// <summary>
///     The User Workflow's adapter on the Processing Run Control sink: it performs every ViewModel write a Processing
///     Run reports (ADR-0012).
/// </summary>
/// <param name="viewModel">The ViewModel the reports are written to.</param>
/// <remarks>
///     <para>
///         Processing Run Control calls each member while holding its lock, so these writes reach the ViewModel in the
///         order they were sequenced (#60). Any of them may raise a notification that re-enters a press.
///     </para>
///     <para>
///         A module of its own that <see cref="UserWorkflow" /> constructs and hands to the Processing Run Control it
///         composes, so its writes can be tested without the rest of the workflow (ADR-0014). It stays separate from
///         <see cref="ViewModelGameContextSink" /> because the two sinks have different ordering contracts.
///     </para>
/// </remarks>
internal sealed class ViewModelProcessingRunControlSink(MainWindowViewModel viewModel) : IProcessingRunControlSink
{
    /// <inheritdoc />
    public void ClearMessageLists()
    {
        viewModel.ClearMessageLists();
    }

    /// <inheritdoc />
    /// <remarks>
    ///     The admitted run still uses its default, but a re-entrant edit made during startup notifications must
    ///     survive for the next run, so the default is written back only while the box still holds the capture.
    /// </remarks>
    public void ReportDefaultedDatabasePath(string defaultedDatabasePath, string? capturedDatabasePath)
    {
        if (string.Equals(viewModel.DatabasePath, capturedDatabasePath, StringComparison.Ordinal))
        {
            viewModel.DatabasePath = defaultedDatabasePath;
        }
    }

    /// <inheritdoc />
    public void ReportRunActivity(ActivityProjection runActivity)
    {
        viewModel.ApplyRunActivityProjection(runActivity);
    }

    /// <inheritdoc />
    public void AddWarningMessage(string message)
    {
        viewModel.AddMessage(MessageList.Warning, message);
    }

    /// <inheritdoc />
    public void AddErrorMessage(string message)
    {
        viewModel.AddMessage(MessageList.Error, message);
    }

    /// <inheritdoc />
    public void AddInformationMessage(string message)
    {
        viewModel.AddMessage(MessageList.Information, message);
    }
}
