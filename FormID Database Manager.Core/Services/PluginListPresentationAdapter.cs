using FormID_Database_Manager.Models;
using FormID_Database_Manager.ViewModels;

namespace FormID_Database_Manager.Services;

/// <summary>
///     Projects authoritative Plugin List facts into Main Window presentation state on the UI dispatcher.
/// </summary>
internal sealed class PluginListPresentationAdapter : IDisposable
{
    private readonly IThreadDispatcher _dispatcher;
    private readonly object _gate = new();
    private readonly PluginList _pluginList;
    private readonly MainWindowViewModel _viewModel;
    private bool _disposed;
    private long _lastPresentedActivityRevision = -1;
    private long _lastPresentedStateRevision = -1;

    /// <summary>
    ///     Subscribes one Main Window projection to a workflow-scoped Plugin List.
    /// </summary>
    /// <param name="pluginList">The authoritative source of immutable Plugin List facts.</param>
    /// <param name="viewModel">The presentation state updated by this adapter.</param>
    /// <param name="dispatcher">The dispatcher that owns all ViewModel mutation.</param>
    /// <exception cref="ArgumentNullException">Any dependency is null.</exception>
    public PluginListPresentationAdapter(
        PluginList pluginList,
        MainWindowViewModel viewModel,
        IThreadDispatcher dispatcher)
    {
        _pluginList = pluginList ?? throw new ArgumentNullException(nameof(pluginList));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

        _pluginList.Changed += OnPluginListChanged;
        QueueProjection();
    }

    /// <summary>
    ///     Unsubscribes this projection and prevents already-queued callbacks from changing presentation state.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _pluginList.Changed -= OnPluginListChanged;
        }
    }

    private void OnPluginListChanged(object? sender, EventArgs eventArgs)
    {
        QueueProjection();
    }

    /// <summary>
    ///     Posts an invalidation callback unless disposal has already detached this presentation lifetime.
    /// </summary>
    private void QueueProjection()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            // Changed is only invalidation: defer reading Current until dispatcher work actually executes.
            _dispatcher.Post(ProjectCurrent);
        }
    }

    /// <summary>
    ///     Reads and applies the latest unpublished state revision while executing on the UI dispatcher.
    /// </summary>
    private void ProjectCurrent()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var state = _pluginList.Current;
            if (state.StateRevision <= _lastPresentedStateRevision)
            {
                return;
            }

            var hasNewActivity = state.ActivityRevision > _lastPresentedActivityRevision;
            // Membership must be visible before its activity, and revisions commit only after every projection succeeds.
            ProjectPlugins(state.Confirmed);
            if (hasNewActivity)
            {
                ProjectActivity(state);
            }

            _lastPresentedStateRevision = state.StateRevision;
            if (hasNewActivity)
            {
                _lastPresentedActivityRevision = state.ActivityRevision;
            }
        }
    }

    /// <summary>
    ///     Replaces changed membership projections and updates selection in place for selection-only revisions.
    /// </summary>
    /// <param name="confirmed">The optional confirmed membership to project.</param>
    private void ProjectPlugins(ConfirmedPluginList? confirmed)
    {
        if (confirmed is null)
        {
            _viewModel.ReplacePluginProjection([]);
            return;
        }

        var selectedNames = new HashSet<string>(confirmed.SelectedPluginNames, StringComparer.OrdinalIgnoreCase);
        if (ProjectionMatches(confirmed))
        {
            foreach (var projected in _viewModel.Plugins)
            {
                projected.IsSelected = selectedNames.Contains(projected.Name);
            }

            return;
        }

        var projectedItems = new List<PluginListItem>(confirmed.Entries.Length);
        foreach (var entry in confirmed.Entries)
        {
            projectedItems.Add(new PluginListItem
            {
                Name = entry.Name,
                IsSelected = selectedNames.Contains(entry.Name),
                MembershipVersion = confirmed.MembershipVersion
            });
        }

        _viewModel.ReplacePluginProjection(projectedItems);
    }

    /// <summary>
    ///     Tests whether the current projection represents the same ordered membership and version.
    /// </summary>
    /// <param name="confirmed">The confirmed membership being projected.</param>
    /// <returns><see langword="true" /> when only projected selection can differ.</returns>
    private bool ProjectionMatches(ConfirmedPluginList confirmed)
    {
        if (_viewModel.Plugins.Count != confirmed.Entries.Length)
        {
            return false;
        }

        for (var index = 0; index < confirmed.Entries.Length; index++)
        {
            var projected = _viewModel.Plugins[index];
            if (projected.MembershipVersion != confirmed.MembershipVersion ||
                !string.Equals(projected.Name, confirmed.Entries[index].Name, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Maps one new UI-neutral activity occurrence to existing Main Window progress and terminal presentation.
    /// </summary>
    /// <param name="state">The current Plugin List state containing the activity and optional confirmation.</param>
    /// <remarks>
    ///     Scan activity is the one Workflow Activity fact this module writes, and it is the only module that writes it.
    ///     A Processing Run's activity is projected separately by the User Workflow, so a scan report can no longer
    ///     overwrite a run's progress; the ViewModel's precedence rule decides which of the two the user sees.
    /// </remarks>
    private void ProjectActivity(PluginListState state)
    {
        switch (state.Activity)
        {
            case PluginListNoSourceActivity:
            case PluginListCancelledActivity:
            case PluginListFaultedActivity:
                // Callers report unexpected exceptions; these message-free terminal facts only clear transient scanning UI.
                ClearScanningState();
                return;

            case PluginListRefreshingActivity refreshing:
                // This module renders its own scanning wording: the counts and their phrasing are Plugin List
                // detail that nothing outside here knows how to say.
                if (refreshing.ScannedCount == 0 || refreshing.TotalCount == 0)
                {
                    _viewModel.ApplyScanActivityProjection(new ActivityProjection(true, "Scanning plugins...", 0));
                    return;
                }

                _viewModel.ApplyScanActivityProjection(new ActivityProjection(
                    true,
                    $"Scanning plugins... ({refreshing.ScannedCount}/{refreshing.TotalCount})",
                    (double)refreshing.ScannedCount / refreshing.TotalCount * 100));
                return;

            case PluginListReadyActivity:
                ClearScanningState();
                var confirmed = state.Confirmed ??
                                throw new InvalidOperationException("Ready Plugin List state requires confirmed membership.");
                var pluginLabel = confirmed.AdvancedMode == AdvancedMode.On
                    ? "plugins"
                    : "non-base game plugins";
                _viewModel.AddInformationMessage($"Loaded {confirmed.Entries.Length} {pluginLabel}");
                return;

            case PluginListFailedActivity failed:
                ClearScanningState();
                var message = string.IsNullOrWhiteSpace(failed.ErrorMessage)
                    ? "Unknown error"
                    : failed.ErrorMessage;
                _viewModel.AddErrorMessage($"Failed to load plugins: {message}");
                _viewModel.AddErrorMessage("Ensure you selected the correct game Data directory");
                return;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(state),
                    state.Activity,
                    "Unsupported Plugin List presentation activity.");
        }
    }

    private void ClearScanningState()
    {
        _viewModel.ApplyScanActivityProjection(ActivityProjection.None);
    }
}
