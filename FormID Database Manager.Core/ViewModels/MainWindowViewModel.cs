using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using FormID_Database_Manager.Models;
using FormID_Database_Manager.Services;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    /// <summary>How many of its most recent messages each Message List keeps.</summary>
    private const int MaxMessagesPerList = 10;

    private readonly ObservableCollection<string> _detectedDirectories = [];
    private readonly IThreadDispatcher _dispatcher;

    private readonly ObservableCollection<PluginListItem> _filteredPlugins = [];

    private readonly ObservableCollection<PluginListItem> _plugins = [];

    [ObservableProperty] private string _databasePath = string.Empty;

    /// <summary>
    ///     Whether the next Processing Run should report what it would do instead of doing it.
    /// </summary>
    /// <remarks>
    ///     Editable by the user like <see cref="UpdateMode" /> rather than projected like the Game Context values: it is
    ///     an input to the next run, not a fact the User Workflow publishes about the current one.
    /// </remarks>
    [ObservableProperty] private bool _dryRun;

    /// <summary>
    ///     The Error Message List, written only through <see cref="AddMessage" /> and
    ///     <see cref="ClearMessageLists" /> (ADR-0014).
    /// </summary>
    private readonly ObservableCollection<string> _errorMessages = [];

    private bool _filterSuspended;

    [ObservableProperty] private string _formIdListPath = string.Empty;

    /// <summary>
    ///     The Information Message List, written only through <see cref="AddMessage" /> and
    ///     <see cref="ClearMessageLists" /> (ADR-0014).
    /// </summary>
    private readonly ObservableCollection<string> _informationMessages = [];

    private int _isApplyingFilter;
    private bool _isApplyingGameContextProjection;

    [ObservableProperty] private string _pluginFilter = string.Empty;

    /// <summary>The Processing Run's Workflow Activity, written only by the User Workflow.</summary>
    private ActivityProjection _runActivity = ActivityProjection.None;

    /// <summary>The Plugin List refresh's Workflow Activity, written only by the Plugin List Presentation Adapter.</summary>
    private ActivityProjection _scanActivity = ActivityProjection.None;

    [ObservableProperty] private bool _updateMode;

    /// <summary>
    ///     The Warning Message List, written only through <see cref="AddMessage" /> and
    ///     <see cref="ClearMessageLists" /> (ADR-0014).
    /// </summary>
    private readonly ObservableCollection<string> _warningMessages = [];

    /// <summary>
    ///     Initializes the ViewModel around the dispatcher that owns every projection it publishes.
    /// </summary>
    /// <param name="dispatcher">
    ///     The dispatcher every projection marshals through. Required: a caller that could omit it would silently opt out
    ///     of the UI-thread marshalling invariant the projections depend on.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="dispatcher" /> is null.</exception>
    public MainWindowViewModel(IThreadDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _dispatcher = dispatcher;
        DetectedDirectories = new ReadOnlyObservableCollection<string>(_detectedDirectories);
        Plugins = new ReadOnlyObservableCollection<PluginListItem>(_plugins);
        FilteredPlugins = new ReadOnlyObservableCollection<PluginListItem>(_filteredPlugins);
        ErrorMessages = new ReadOnlyObservableCollection<string>(_errorMessages);
        WarningMessages = new ReadOnlyObservableCollection<string>(_warningMessages);
        InformationMessages = new ReadOnlyObservableCollection<string>(_informationMessages);
        _plugins.CollectionChanged += OnPluginsCollectionChanged;

        // Sourced from the Supported GameRelease table, which owns both the content and the display order, so a
        // release added there appears in the dropdown without a second list to remember (ADR-0003).
        AvailableGames = SupportedGameReleases.All
            .Select(static supported => supported.Release)
            .ToList()
            .AsReadOnly();

        _detectedDirectories.CollectionChanged += (_, _) =>
        {
            if (!_isApplyingGameContextProjection)
            {
                OnPropertyChanged(nameof(HasMultipleDirectories));
            }
        };
        // The Message List collections are readonly and never replaced, so one subscription for the ViewModel's
        // lifetime keeps each Has*Messages flag current; no replacement hook has to move it (ADR-0014).
        _errorMessages.CollectionChanged += OnErrorMessagesCollectionChanged;
        _informationMessages.CollectionChanged += OnInformationMessagesCollectionChanged;
        _warningMessages.CollectionChanged += OnWarningMessagesCollectionChanged;
    }

    public IReadOnlyList<GameRelease> AvailableGames { get; }

    /// <summary>
    ///     Gets the read-only ordered projection of directories available for the current Game Context.
    /// </summary>
    /// <remarks>The observable collection identity is stable for the lifetime of this ViewModel.</remarks>
    public ReadOnlyObservableCollection<string> DetectedDirectories { get; }

    /// <summary>
    ///     Gets the projected Advanced Mode value.
    /// </summary>
    public bool AdvancedMode { get; private set; }

    /// <summary>
    ///     Gets the projected game-directory presentation value.
    /// </summary>
    public string GameDirectory { get; private set; } = string.Empty;

    /// <summary>
    ///     Gets the projected GameRelease.
    /// </summary>
    public GameRelease? SelectedGame { get; private set; }

    public bool IsGameSelected => SelectedGame.HasValue;

    public bool HasMultipleDirectories => DetectedDirectories.Count > 1;

    /// <summary>
    ///     Gets the read-only Error Message List. Writers append through <see cref="AddMessage" /> and start over through
    ///     <see cref="ClearMessageLists" /> (ADR-0014).
    /// </summary>
    /// <remarks>The observable collection identity is stable for the lifetime of this ViewModel.</remarks>
    public ReadOnlyObservableCollection<string> ErrorMessages { get; }

    /// <summary>
    ///     Gets the read-only Warning Message List. Writers append through <see cref="AddMessage" /> and start over
    ///     through <see cref="ClearMessageLists" /> (ADR-0014).
    /// </summary>
    /// <remarks>The observable collection identity is stable for the lifetime of this ViewModel.</remarks>
    public ReadOnlyObservableCollection<string> WarningMessages { get; }

    /// <summary>
    ///     Gets the read-only Information Message List. Writers append through <see cref="AddMessage" /> and start over
    ///     through <see cref="ClearMessageLists" /> (ADR-0014).
    /// </summary>
    /// <remarks>The observable collection identity is stable for the lifetime of this ViewModel.</remarks>
    public ReadOnlyObservableCollection<string> InformationMessages { get; }

    public bool HasErrorMessages => ErrorMessages.Count > 0;

    public bool HasInformationMessages => InformationMessages.Count > 0;

    public bool HasWarningMessages => WarningMessages.Count > 0;

    /// <summary>
    ///     Gets the Workflow Activity that currently owns the progress channel.
    /// </summary>
    /// <remarks>
    ///     This is the precedence rule, and the only place it is stated: run activity owns the channel while it is active,
    ///     scan activity shows otherwise, and neither active shows nothing. Everything the channel presents is derived
    ///     from here, so changing which activity wins is one edit. The end-of-run fallback needs no special case — a run
    ///     clearing its activity simply stops winning, and a still-scanning refresh takes the channel back.
    /// </remarks>
    private ActivityProjection CurrentActivity => _runActivity.IsActive
        ? _runActivity
        : _scanActivity.IsActive
            ? _scanActivity
            : ActivityProjection.None;

    /// <summary>
    ///     Gets the status text of the Workflow Activity that owns the progress channel.
    /// </summary>
    public string ProgressStatus => CurrentActivity.Status;

    /// <summary>
    ///     Gets the progress percentage of the Workflow Activity that owns the progress channel.
    /// </summary>
    public double ProgressValue => CurrentActivity.Value;

    /// <summary>
    ///     Gets whether any Workflow Activity is currently reporting, so the progress row has something to show.
    /// </summary>
    public bool IsProgressVisible => CurrentActivity.IsActive;

    /// <summary>
    ///     Gets the process button caption for what pressing it will do.
    /// </summary>
    /// <remarks>
    ///     Derived here rather than rendered by the reporting module because it comes from a single boolean. Status text
    ///     encodes domain detail such as phase and Plugin counts, which is why that stays with the module reporting it.
    /// </remarks>
    public string ProcessButtonText => _runActivity.IsActive ? "Cancel Processing" : "Process FormIDs";

    /// <summary>
    ///     Gets the read-only Main Window projection published by the Plugin List presentation adapter.
    /// </summary>
    /// <remarks>The observable collection identity is stable for the lifetime of this ViewModel.</remarks>
    public ReadOnlyObservableCollection<PluginListItem> Plugins { get; }

    /// <summary>
    ///     Gets the membership version of the Confirmed Plugin List currently displayed, or <see langword="null" /> when
    ///     none is displayed.
    /// </summary>
    /// <remarks>
    ///     Set only by <see cref="ReplacePluginProjection" />. It lags the Plugin List's own confirmed version while a
    ///     newer membership's projection is still queued on the dispatcher, which is what lets a whole-list selection
    ///     carry the version the user actually saw.
    /// </remarks>
    internal long? ProjectedMembershipVersion { get; private set; }

    /// <summary>
    ///     Gets the read-only filtered view of the current Plugin List projection.
    /// </summary>
    public ReadOnlyObservableCollection<PluginListItem> FilteredPlugins { get; }

    private void OnPluginsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ApplyFilter();
    }

    /// <summary>
    ///     Projects the Processing Run's Workflow Activity through the dispatcher that owns this ViewModel.
    /// </summary>
    /// <param name="runActivity">The User Workflow's complete already-rendered run report.</param>
    /// <remarks>The User Workflow is the only writer of this fact; nothing else may call this.</remarks>
    internal void ApplyRunActivityProjection(ActivityProjection runActivity)
    {
        if (!_dispatcher.CheckAccess())
        {
            // One posted action, as with Game Context: an observer must never pair one activity's status with
            // another's progress value.
            _dispatcher.Post(() => ApplyRunActivityProjectionCore(runActivity));
            return;
        }

        ApplyRunActivityProjectionCore(runActivity);
    }

    /// <summary>
    ///     Projects the Plugin List refresh's Workflow Activity through the dispatcher that owns this ViewModel.
    /// </summary>
    /// <param name="scanActivity">The Plugin List Presentation Adapter's complete already-rendered scan report.</param>
    /// <remarks>The Plugin List Presentation Adapter is the only writer of this fact; nothing else may call this.</remarks>
    internal void ApplyScanActivityProjection(ActivityProjection scanActivity)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Post(() => ApplyScanActivityProjectionCore(scanActivity));
            return;
        }

        ApplyScanActivityProjectionCore(scanActivity);
    }

    /// <summary>
    ///     Applies one run report and raises notifications only after the backing fact is current.
    /// </summary>
    /// <param name="runActivity">The complete run report to make authoritative.</param>
    private void ApplyRunActivityProjectionCore(ActivityProjection runActivity)
    {
        var previousChannel = CurrentActivity;
        var previousRunActive = _runActivity.IsActive;

        // The backing field is written before any notification so observers always read one complete snapshot.
        _runActivity = runActivity;

        RaiseChannelNotifications(previousChannel);
        if (previousRunActive != runActivity.IsActive)
        {
            OnPropertyChanged(nameof(ProcessButtonText));
        }
    }

    /// <summary>
    ///     Applies one scan report and raises notifications only after the backing fact is current.
    /// </summary>
    /// <param name="scanActivity">The complete scan report to make authoritative.</param>
    private void ApplyScanActivityProjectionCore(ActivityProjection scanActivity)
    {
        var previousChannel = CurrentActivity;

        // The backing field is written before any notification so observers always read one complete snapshot.
        _scanActivity = scanActivity;

        RaiseChannelNotifications(previousChannel);
    }

    /// <summary>
    ///     Raises a notification for each channel value the precedence rule now resolves differently.
    /// </summary>
    /// <param name="previousChannel">The activity that owned the channel before the applied projection.</param>
    /// <remarks>
    ///     Comparing resolved channel values rather than the projected fact is what makes the losing activity silent:
    ///     a refresh reporting underneath an active run changes no bound value, so no notification is raised at all.
    /// </remarks>
    private void RaiseChannelNotifications(ActivityProjection previousChannel)
    {
        var currentChannel = CurrentActivity;

        if (!string.Equals(previousChannel.Status, currentChannel.Status, StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(ProgressStatus));
        }

        // Exact comparison is right here: these values are carried through unchanged rather than computed, so an
        // equal projection is the same double, and a tolerance would only suppress a genuinely tiny change.
        if (previousChannel.Value != currentChannel.Value)
        {
            OnPropertyChanged(nameof(ProgressValue));
        }

        if (previousChannel.IsActive != currentChannel.IsActive)
        {
            OnPropertyChanged(nameof(IsProgressVisible));
        }
    }

    /// <summary>
    ///     Projects one complete Game Context through the dispatcher that owns this ViewModel.
    /// </summary>
    /// <param name="selectedGame">The selected GameRelease, or null when no release is selected.</param>
    /// <param name="gameDirectory">The selected domain directory, or null when the context is incomplete.</param>
    /// <param name="availableDirectories">The complete ordered available-directory snapshot.</param>
    /// <param name="advancedMode">The authoritative Advanced Mode value.</param>
    /// <exception cref="ArgumentNullException"><paramref name="availableDirectories" /> is null.</exception>
    internal void ApplyGameContextProjection(
        GameRelease? selectedGame,
        string? gameDirectory,
        IReadOnlyList<string> availableDirectories,
        AdvancedMode advancedMode)
    {
        ArgumentNullException.ThrowIfNull(availableDirectories);
        var directorySnapshot = availableDirectories.ToArray();

        if (!_dispatcher.CheckAccess())
        {
            // A single posted action prevents observers from seeing fields from different Game Context snapshots.
            _dispatcher.Post(() => ApplyGameContextProjectionCore(
                selectedGame,
                gameDirectory,
                directorySnapshot,
                advancedMode));
            return;
        }

        ApplyGameContextProjectionCore(selectedGame, gameDirectory, directorySnapshot, advancedMode);
    }

    /// <summary>
    ///     Applies a materialized Game Context snapshot and raises notifications only after every field is current.
    /// </summary>
    /// <param name="selectedGame">The selected GameRelease, or null when no release is selected.</param>
    /// <param name="gameDirectory">The selected domain directory, or null when the context is incomplete.</param>
    /// <param name="availableDirectories">The complete ordered available-directory snapshot.</param>
    /// <param name="advancedMode">The authoritative Advanced Mode value.</param>
    private void ApplyGameContextProjectionCore(
        GameRelease? selectedGame,
        string? gameDirectory,
        IReadOnlyList<string> availableDirectories,
        AdvancedMode advancedMode)
    {
        var presentationDirectory = gameDirectory ?? string.Empty;
        var presentationAdvancedMode = advancedMode == Services.AdvancedMode.On;
        var selectedGameChanged = SelectedGame != selectedGame;
        var gameDirectoryChanged = !string.Equals(GameDirectory, presentationDirectory, StringComparison.Ordinal);
        var availableDirectoriesChanged = !_detectedDirectories.SequenceEqual(availableDirectories);
        var advancedModeChanged = AdvancedMode != presentationAdvancedMode;

        // Update every backing value before notifications so observers always read one complete snapshot.
        SelectedGame = selectedGame;
        GameDirectory = presentationDirectory;
        AdvancedMode = presentationAdvancedMode;

        if (availableDirectoriesChanged)
        {
            _isApplyingGameContextProjection = true;
            try
            {
                _detectedDirectories.Clear();
                foreach (var availableDirectory in availableDirectories)
                {
                    _detectedDirectories.Add(availableDirectory);
                }
            }
            finally
            {
                _isApplyingGameContextProjection = false;
            }

            OnPropertyChanged(nameof(HasMultipleDirectories));
        }

        if (selectedGameChanged)
        {
            OnPropertyChanged(nameof(SelectedGame));
            OnPropertyChanged(nameof(IsGameSelected));
        }

        if (gameDirectoryChanged)
        {
            OnPropertyChanged(nameof(GameDirectory));
        }

        if (advancedModeChanged)
        {
            OnPropertyChanged(nameof(AdvancedMode));
        }
    }

    /// <summary>
    ///     Replaces projected Plugin items as one UI-dispatched membership update, then reapplies the current text filter.
    /// </summary>
    /// <param name="membershipVersion">
    ///     The membership version of the Confirmed Plugin List being displayed, or <see langword="null" /> when no
    ///     Confirmed Plugin List is displayed. It is recorded as given, never derived from the items, because an empty
    ///     displayed list still has a version.
    /// </param>
    /// <param name="projectedItems">The ordered presentation items copied from current confirmed membership.</param>
    /// <exception cref="ArgumentNullException"><paramref name="projectedItems" /> is null.</exception>
    /// <remarks>The caller must run on the dispatcher that owns this ViewModel.</remarks>
    internal void ReplacePluginProjection(long? membershipVersion, IEnumerable<PluginListItem> projectedItems)
    {
        ArgumentNullException.ThrowIfNull(projectedItems);

        ProjectedMembershipVersion = membershipVersion;
        _filterSuspended = true;
        try
        {
            _plugins.Clear();
            foreach (var item in projectedItems)
            {
                _plugins.Add(item);
            }
        }
        finally
        {
            _filterSuspended = false;
            ApplyFilter();
        }
    }

    private void OnErrorMessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasErrorMessages));
    }

    private void OnInformationMessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasInformationMessages));
    }

    private void OnWarningMessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasWarningMessages));
    }

    partial void OnPluginFilterChanged(string value)
    {
        // Applied on every keystroke with no delay path behind it: ApplyFilter reconciles the existing collection in
        // place rather than rebuilding it, so the per-change cost does not justify a timer and the cancellation source
        // one would own.
        ApplyFilter(value);
    }

    private void ApplyFilter()
    {
        ApplyFilter(PluginFilter);
    }

    /// <summary>
    ///     Reconciles visible Plugin items in authoritative projection order without replacing surviving item instances.
    /// </summary>
    /// <param name="pluginFilter">The case-insensitive Plugin-name fragment to display.</param>
    private void ApplyFilter(string pluginFilter)
    {
        // Skip filter application when suspended (during bulk loading)
        if (_filterSuspended)
        {
            return;
        }

        // Prevent recursive calls using atomic compare-exchange for thread safety
        if (Interlocked.CompareExchange(ref _isApplyingFilter, 1, 0) != 0)
        {
            return;
        }

        // Ensure filter operations happen on UI thread
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Post(ApplyFilter);
            Interlocked.Exchange(ref _isApplyingFilter, 0);
            return;
        }

        try
        {
            // Reconcile the existing collection in Plugin List order so filtering preserves item identity and selection.
            var filtered = string.IsNullOrWhiteSpace(pluginFilter)
                ? _plugins.ToList()
                : _plugins.Where(p => p.Name.Contains(pluginFilter, StringComparison.OrdinalIgnoreCase)).ToList();

            var filteredSet = new HashSet<PluginListItem>(filtered);
            for (var index = _filteredPlugins.Count - 1; index >= 0; index--)
            {
                if (!filteredSet.Contains(_filteredPlugins[index]))
                {
                    _filteredPlugins.RemoveAt(index);
                }
            }

            for (var index = 0; index < filtered.Count; index++)
            {
                var item = filtered[index];
                if (index < _filteredPlugins.Count && ReferenceEquals(_filteredPlugins[index], item))
                {
                    continue;
                }

                // Insertions also repair order changes; any displaced duplicate is trimmed from the tail below.
                _filteredPlugins.Insert(index, item);
            }

            while (_filteredPlugins.Count > filtered.Count)
            {
                _filteredPlugins.RemoveAt(_filteredPlugins.Count - 1);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _isApplyingFilter, 0);
        }
    }

    /// <summary>
    ///     Appends one message to a Message List, keeping only the most recent <see cref="MaxMessagesPerList" />.
    /// </summary>
    /// <param name="list">The Message List the message belongs to.</param>
    /// <param name="message">The text the user reads.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="list" /> is not a defined Message List.</exception>
    /// <remarks>
    ///     <para>
    ///         Every writer appends through here, so appending has one dispatch rule and one cap (ADR-0014). A call made
    ///         off the dispatcher's thread is posted to it rather than applied in place.
    ///     </para>
    ///     <para>
    ///         There is deliberately no lock. Every Message List mutation, this one and
    ///         <see cref="ClearMessageLists" />, is posted to the dispatcher before it touches a list, so all
    ///         of them run on the dispatcher's one thread, one after another. A lock taken after that post could never
    ///         be contended, and would only suggest a threading model the lists do not have.
    ///     </para>
    /// </remarks>
    internal void AddMessage(MessageList list, string message)
    {
        // Validated before dispatch so an off-thread caller is refused where it made the mistake, not by a posted
        // action failing later on the UI thread.
        if (!Enum.IsDefined(list))
        {
            throw new ArgumentOutOfRangeException(nameof(list), list, "Unsupported Message List.");
        }

        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Post(() => AddMessage(list, message));
            return;
        }

        var messages = MessagesFor(list);
        messages.Add(message);

        if (messages.Count > MaxMessagesPerList)
        {
            messages.RemoveAt(0);
        }
    }

    /// <summary>
    ///     Starts the Message Lists over: clears errors, warnings, and information.
    /// </summary>
    /// <remarks>
    ///     This names what happens, not when. Processing Run Control decides when: on every press of the run button
    ///     that does not cancel a run, including a refused one (ADR-0012, ADR-0014). Information is cleared with the
    ///     other two lists so the user reads only what the new press produced, and earlier runs' completion lines,
    ///     plans, and Game Context notices cannot pile up (#95). It follows the same dispatch rule as
    ///     <see cref="AddMessage" />, so a clear and an append can never interleave on two threads.
    /// </remarks>
    internal void ClearMessageLists()
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Post(ClearMessageLists);
            return;
        }

        _errorMessages.Clear();
        _warningMessages.Clear();
        _informationMessages.Clear();
    }

    /// <summary>
    ///     Resolves a Message List to the collection that shows it.
    /// </summary>
    /// <param name="list">A defined Message List; <see cref="AddMessage" /> has already refused any other value.</param>
    /// <returns>The private, writable collection behind that list.</returns>
    private ObservableCollection<string> MessagesFor(MessageList list)
    {
        return list switch
        {
            MessageList.Error => _errorMessages,
            MessageList.Warning => _warningMessages,
            MessageList.Information => _informationMessages,
            _ => throw new UnreachableException($"AddMessage admitted an undefined Message List: {list}.")
        };
    }
}
