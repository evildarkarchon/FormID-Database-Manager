using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using FormID_Database_Manager.Models;
using FormID_Database_Manager.Services;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IThreadDispatcher _dispatcher;
    private readonly Lock _messagesLock = new();
    private bool _isApplyingGameContextProjection;

    private bool _advancedMode;

    [ObservableProperty] private string _databasePath = string.Empty;

    private readonly ObservableCollection<string> _detectedDirectories = [];

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasErrorMessages))]
    private ObservableCollection<string> _errorMessages = [];

    private readonly ObservableCollection<PluginListItem> _filteredPlugins = [];

    [ObservableProperty] private string _formIdListPath = string.Empty;

    private string _gameDirectory = string.Empty;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasInformationMessages))]
    private ObservableCollection<string> _informationMessages = [];

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasWarningMessages))]
    private ObservableCollection<string> _warningMessages = [];

    private bool _filterSuspended;
    private int _isApplyingFilter;

    [ObservableProperty] private string _pluginFilter = string.Empty;

    private readonly ObservableCollection<PluginListItem> _plugins = [];

    /// <summary>The Processing Run's Workflow Activity, written only by the User Workflow.</summary>
    private ActivityProjection _runActivity = ActivityProjection.None;

    /// <summary>The Plugin List refresh's Workflow Activity, written only by the Plugin List Presentation Adapter.</summary>
    private ActivityProjection _scanActivity = ActivityProjection.None;

    private GameRelease? _selectedGame;

    [ObservableProperty] private bool _updateMode;

    /// <summary>
    /// Initializes the ViewModel around the dispatcher that owns every projection it publishes.
    /// </summary>
    /// <param name="dispatcher">
    /// The dispatcher every projection marshals through. Required: a caller that could omit it would silently opt out
    /// of the UI-thread marshalling invariant the projections depend on.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="dispatcher" /> is null.</exception>
    public MainWindowViewModel(IThreadDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _dispatcher = dispatcher;
        DetectedDirectories = new ReadOnlyObservableCollection<string>(_detectedDirectories);
        Plugins = new ReadOnlyObservableCollection<PluginListItem>(_plugins);
        FilteredPlugins = new ReadOnlyObservableCollection<PluginListItem>(_filteredPlugins);
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
        _errorMessages.CollectionChanged += OnErrorMessagesCollectionChanged;
        _informationMessages.CollectionChanged += OnInformationMessagesCollectionChanged;
        _warningMessages.CollectionChanged += OnWarningMessagesCollectionChanged;
    }

    private void OnPluginsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ApplyFilter();
    }

    public IReadOnlyList<GameRelease> AvailableGames { get; }

    /// <summary>
    /// Gets the read-only ordered projection of directories available for the current Game Context.
    /// </summary>
    /// <remarks>The observable collection identity is stable for the lifetime of this ViewModel.</remarks>
    public ReadOnlyObservableCollection<string> DetectedDirectories { get; }

    /// <summary>
    /// Gets the projected Advanced Mode value.
    /// </summary>
    public bool AdvancedMode => _advancedMode;

    /// <summary>
    /// Gets the projected game-directory presentation value.
    /// </summary>
    public string GameDirectory => _gameDirectory;

    /// <summary>
    /// Gets the projected GameRelease.
    /// </summary>
    public GameRelease? SelectedGame => _selectedGame;

    public bool IsGameSelected => SelectedGame.HasValue;

    public bool HasMultipleDirectories => DetectedDirectories.Count > 1;

    public bool HasErrorMessages => ErrorMessages.Count > 0;

    public bool HasInformationMessages => InformationMessages.Count > 0;

    public bool HasWarningMessages => WarningMessages.Count > 0;

    /// <summary>
    /// Gets the Workflow Activity that currently owns the progress channel.
    /// </summary>
    /// <remarks>
    /// This is the precedence rule, and the only place it is stated: run activity owns the channel while it is active,
    /// scan activity shows otherwise, and neither active shows nothing. Everything the channel presents is derived
    /// from here, so changing which activity wins is one edit. The end-of-run fallback needs no special case — a run
    /// clearing its activity simply stops winning, and a still-scanning refresh takes the channel back.
    /// </remarks>
    private ActivityProjection CurrentActivity => _runActivity.IsActive
        ? _runActivity
        : _scanActivity.IsActive
            ? _scanActivity
            : ActivityProjection.None;

    /// <summary>
    /// Gets the status text of the Workflow Activity that owns the progress channel.
    /// </summary>
    public string ProgressStatus => CurrentActivity.Status;

    /// <summary>
    /// Gets the progress percentage of the Workflow Activity that owns the progress channel.
    /// </summary>
    public double ProgressValue => CurrentActivity.Value;

    /// <summary>
    /// Gets whether any Workflow Activity is currently reporting, so the progress row has something to show.
    /// </summary>
    public bool IsProgressVisible => CurrentActivity.IsActive;

    /// <summary>
    /// Gets the process button caption for what pressing it will do.
    /// </summary>
    /// <remarks>
    /// Derived here rather than rendered by the reporting module because it comes from a single boolean. Status text
    /// encodes domain detail such as phase and Plugin counts, which is why that stays with the module reporting it.
    /// </remarks>
    public string ProcessButtonText => _runActivity.IsActive ? "Cancel Processing" : "Process FormIDs";

    /// <summary>
    /// Projects the Processing Run's Workflow Activity through the dispatcher that owns this ViewModel.
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
    /// Projects the Plugin List refresh's Workflow Activity through the dispatcher that owns this ViewModel.
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
    /// Applies one run report and raises notifications only after the backing fact is current.
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
    /// Applies one scan report and raises notifications only after the backing fact is current.
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
    /// Raises a notification for each channel value the precedence rule now resolves differently.
    /// </summary>
    /// <param name="previousChannel">The activity that owned the channel before the applied projection.</param>
    /// <remarks>
    /// Comparing resolved channel values rather than the projected fact is what makes the losing activity silent:
    /// a refresh reporting underneath an active run changes no bound value, so no notification is raised at all.
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
    /// Projects one complete Game Context through the dispatcher that owns this ViewModel.
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
    /// Applies a materialized Game Context snapshot and raises notifications only after every field is current.
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
        var presentationAdvancedMode = advancedMode == FormID_Database_Manager.Services.AdvancedMode.On;
        var selectedGameChanged = _selectedGame != selectedGame;
        var gameDirectoryChanged = !string.Equals(_gameDirectory, presentationDirectory, StringComparison.Ordinal);
        var availableDirectoriesChanged = !_detectedDirectories.SequenceEqual(availableDirectories);
        var advancedModeChanged = _advancedMode != presentationAdvancedMode;

        // Update every backing value before notifications so observers always read one complete snapshot.
        _selectedGame = selectedGame;
        _gameDirectory = presentationDirectory;
        _advancedMode = presentationAdvancedMode;

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
    ///     Gets the read-only Main Window projection published by the Plugin List presentation adapter.
    /// </summary>
    /// <remarks>The observable collection identity is stable for the lifetime of this ViewModel.</remarks>
    public ReadOnlyObservableCollection<PluginListItem> Plugins { get; }

    /// <summary>
    ///     Gets the read-only filtered view of the current Plugin List projection.
    /// </summary>
    public ReadOnlyObservableCollection<PluginListItem> FilteredPlugins { get; }

    /// <summary>
    ///     Replaces projected Plugin items as one UI-dispatched membership update, then reapplies the current text filter.
    /// </summary>
    /// <param name="projectedItems">The ordered presentation items copied from current confirmed membership.</param>
    /// <exception cref="ArgumentNullException"><paramref name="projectedItems" /> is null.</exception>
    /// <remarks>The caller must run on the dispatcher that owns this ViewModel.</remarks>
    internal void ReplacePluginProjection(IEnumerable<PluginListItem> projectedItems)
    {
        ArgumentNullException.ThrowIfNull(projectedItems);

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

    partial void OnErrorMessagesChanging(ObservableCollection<string> value)
    {
        if (ReferenceEquals(ErrorMessages, value))
        {
            return;
        }

        ErrorMessages.CollectionChanged -= OnErrorMessagesCollectionChanged;
    }

    partial void OnErrorMessagesChanged(ObservableCollection<string> value)
    {
        value.CollectionChanged += OnErrorMessagesCollectionChanged;
        OnPropertyChanged(nameof(HasErrorMessages));
    }

    partial void OnInformationMessagesChanging(ObservableCollection<string> value)
    {
        if (ReferenceEquals(InformationMessages, value))
        {
            return;
        }

        InformationMessages.CollectionChanged -= OnInformationMessagesCollectionChanged;
    }

    partial void OnInformationMessagesChanged(ObservableCollection<string> value)
    {
        value.CollectionChanged += OnInformationMessagesCollectionChanged;
        OnPropertyChanged(nameof(HasInformationMessages));
    }

    partial void OnWarningMessagesChanging(ObservableCollection<string> value)
    {
        if (ReferenceEquals(WarningMessages, value))
        {
            return;
        }

        WarningMessages.CollectionChanged -= OnWarningMessagesCollectionChanged;
    }

    partial void OnWarningMessagesChanged(ObservableCollection<string> value)
    {
        value.CollectionChanged += OnWarningMessagesCollectionChanged;
        OnPropertyChanged(nameof(HasWarningMessages));
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

    public virtual void AddErrorMessage(string message, int maxMessages = 10)
    {
        // Ensure collection operations happen on UI thread
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Post(() => AddErrorMessage(message, maxMessages));
            return;
        }

        lock (_messagesLock)
        {
            ErrorMessages.Add(message);

            if (ErrorMessages.Count > maxMessages)
            {
                ErrorMessages.RemoveAt(0);
            }
        }
    }

    public virtual void AddInformationMessage(string message, int maxMessages = 10)
    {
        // Ensure collection operations happen on UI thread
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Post(() => AddInformationMessage(message, maxMessages));
            return;
        }

        lock (_messagesLock)
        {
            InformationMessages.Add(message);

            if (InformationMessages.Count > maxMessages)
            {
                InformationMessages.RemoveAt(0);
            }
        }
    }

    public virtual void AddWarningMessage(string message, int maxMessages = 10)
    {
        // Ensure collection operations happen on UI thread
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Post(() => AddWarningMessage(message, maxMessages));
            return;
        }

        lock (_messagesLock)
        {
            WarningMessages.Add(message);

            if (WarningMessages.Count > maxMessages)
            {
                WarningMessages.RemoveAt(0);
            }
        }
    }

}
