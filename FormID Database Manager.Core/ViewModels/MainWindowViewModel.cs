using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using FormID_Database_Manager.Models;
using FormID_Database_Manager.Services;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly int _debounceMs;
    private readonly IThreadDispatcher _dispatcher;
    private readonly Lock _messagesLock = new();
    private CancellationTokenSource? _debounceCts;

    [ObservableProperty] private bool _advancedMode;

    [ObservableProperty] private string _databasePath = string.Empty;

    [ObservableProperty] private ObservableCollection<string> _detectedDirectories = [];

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasErrorMessages))]
    private ObservableCollection<string> _errorMessages = [];

    private readonly ObservableCollection<PluginListItem> _filteredPlugins = [];

    [ObservableProperty] private string _formIdListPath = string.Empty;

    [ObservableProperty] private string _gameDirectory = string.Empty;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasInformationMessages))]
    private ObservableCollection<string> _informationMessages = [];

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasWarningMessages))]
    private ObservableCollection<string> _warningMessages = [];

    private bool _filterSuspended;
    private int _isApplyingFilter;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsProgressVisible))]
    private bool _isProcessing;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsProgressVisible))]
    private bool _isScanning;

    [ObservableProperty] private string _pluginFilter = string.Empty;

    private readonly ObservableCollection<PluginListItem> _plugins = [];

    [ObservableProperty] private string _processButtonText = "Process FormIDs";

    [ObservableProperty] private string _progressStatus = string.Empty;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsGameSelected))]
    private GameRelease? _selectedGame;

    [ObservableProperty] private double _progressValue;

    [ObservableProperty] private bool _updateMode;

    public MainWindowViewModel(IThreadDispatcher? dispatcher = null) : this(dispatcher, 0)
    {
    }

    public MainWindowViewModel(IThreadDispatcher? dispatcher, int debounceMs)
    {
        _dispatcher = dispatcher ?? new ImmediateThreadDispatcher();
        _debounceMs = debounceMs;
        Plugins = new ReadOnlyObservableCollection<PluginListItem>(_plugins);
        FilteredPlugins = new ReadOnlyObservableCollection<PluginListItem>(_filteredPlugins);
        _plugins.CollectionChanged += OnPluginsCollectionChanged;

        AvailableGames = new List<GameRelease>
        {
            GameRelease.Fallout4,
            GameRelease.SkyrimSE,
            GameRelease.SkyrimLE,
            GameRelease.SkyrimVR,
            GameRelease.SkyrimSEGog,
            GameRelease.EnderalSE,
            GameRelease.EnderalLE,
            GameRelease.Fallout4VR,
            GameRelease.Oblivion,
            GameRelease.Starfield
        }.AsReadOnly();

        _detectedDirectories.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasMultipleDirectories));
        _errorMessages.CollectionChanged += OnErrorMessagesCollectionChanged;
        _informationMessages.CollectionChanged += OnInformationMessagesCollectionChanged;
        _warningMessages.CollectionChanged += OnWarningMessagesCollectionChanged;
    }

    private void OnPluginsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ApplyFilter();
    }

    public IReadOnlyList<GameRelease> AvailableGames { get; }

    public bool IsGameSelected => SelectedGame.HasValue;

    public bool HasMultipleDirectories => DetectedDirectories.Count > 1;

    public bool HasErrorMessages => ErrorMessages.Count > 0;

    public bool HasInformationMessages => InformationMessages.Count > 0;

    public bool HasWarningMessages => WarningMessages.Count > 0;

    public bool IsProgressVisible => IsProcessing || IsScanning;

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
        if (_debounceMs <= 0)
        {
            ApplyFilter(value);
        }
        else
        {
            DebounceApplyFilter();
        }
    }

    private void DebounceApplyFilter()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = new CancellationTokenSource();
        var token = _debounceCts.Token;

        Task.Delay(_debounceMs, token).ContinueWith(_ =>
        {
            if (!token.IsCancellationRequested)
            {
                _dispatcher.Post(ApplyFilter);
            }
        }, token, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
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

    public void ResetProgress()
    {
        // Ensure collection operations happen on UI thread
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Post(ResetProgress);
            return;
        }

        ProgressValue = 0;
        ProgressStatus = string.Empty;
        IsProcessing = false;
        lock (_messagesLock)
        {
            ErrorMessages.Clear();
            InformationMessages.Clear();
            WarningMessages.Clear();
        }
    }

    public void UpdateProgress(string status, double? value = null)
    {
        // Ensure UI updates happen on UI thread
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Post(() => UpdateProgress(status, value));
            return;
        }

        ProgressStatus = status;
        if (value.HasValue)
        {
            ProgressValue = value.Value;
        }
    }

    public void Dispose()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;
    }

}
