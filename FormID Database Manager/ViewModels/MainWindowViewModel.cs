using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using FormID_Database_Manager.Models;

namespace FormID_Database_Manager.ViewModels;

public class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly object _pluginsLock = new();
    private string _databasePath = string.Empty;
    private string _detectedGame = string.Empty;
    private ObservableCollection<string> _errorMessages = [];
    private ObservableCollection<PluginListItem> _filteredPlugins = [];
    private string _formIdListPath = string.Empty;
    private string _gameDirectory = string.Empty;
    private ObservableCollection<string> _informationMessages = [];
    private bool _isApplyingFilter;
    private bool _isProcessing;
    private string _pluginFilter = string.Empty;
    private ObservableCollection<PluginListItem> _plugins;
    private string _progressStatus = string.Empty;
    private double _progressValue;

    public MainWindowViewModel()
    {
        _plugins = [];
        _plugins.CollectionChanged += (s, e) => ApplyFilter();
    }

    public string GameDirectory
    {
        get => _gameDirectory;
        set => SetProperty(ref _gameDirectory, value);
    }

    public string DatabasePath
    {
        get => _databasePath;
        set => SetProperty(ref _databasePath, value);
    }

    public string FormIdListPath
    {
        get => _formIdListPath;
        set => SetProperty(ref _formIdListPath, value);
    }

    public string DetectedGame
    {
        get => _detectedGame;
        set => SetProperty(ref _detectedGame, value);
    }

    public bool IsProcessing
    {
        get => _isProcessing;
        set => SetProperty(ref _isProcessing, value);
    }

    public double ProgressValue
    {
        get => _progressValue;
        set => SetProperty(ref _progressValue, value);
    }

    public string ProgressStatus
    {
        get => _progressStatus;
        set => SetProperty(ref _progressStatus, value);
    }

    public ObservableCollection<PluginListItem> Plugins
    {
        get => _plugins;
        set
        {
            if (SetProperty(ref _plugins, value))
            {
                ApplyFilter();
            }
        }
    }

    public ObservableCollection<PluginListItem> FilteredPlugins
    {
        get => _filteredPlugins;
        private set => SetProperty(ref _filteredPlugins, value);
    }

    public string PluginFilter
    {
        get => _pluginFilter;
        set
        {
            if (SetProperty(ref _pluginFilter, value))
            {
                ApplyFilter();
            }
        }
    }

    public ObservableCollection<string> ErrorMessages
    {
        get => _errorMessages;
        set => SetProperty(ref _errorMessages, value);
    }

    public ObservableCollection<string> InformationMessages
    {
        get => _informationMessages;
        set => SetProperty(ref _informationMessages, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void ApplyFilter()
    {
        // Prevent recursive calls
        if (_isApplyingFilter)
        {
            return;
        }

        // Ensure filter operations happen on UI thread
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ApplyFilter());
            return;
        }

        try
        {
            _isApplyingFilter = true;

            // Incremental update approach: modify existing collection instead of recreating
            // This prevents O(n) allocations and excessive UI notifications on every filter change
            List<PluginListItem> filtered;
            lock (_pluginsLock)
            {
                filtered = string.IsNullOrWhiteSpace(_pluginFilter)
                    ? _plugins.ToList()
                    : _plugins.Where(p => p.Name.Contains(_pluginFilter, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            // Use HashSet for O(1) lookup performance
            var filteredSet = new HashSet<PluginListItem>(filtered);

            // Remove items not in filtered set (iterate backwards to avoid index issues)
            for (var i = _filteredPlugins.Count - 1; i >= 0; i--)
            {
                if (!filteredSet.Contains(_filteredPlugins[i]))
                {
                    _filteredPlugins.RemoveAt(i);
                }
            }

            // Add new items that aren't already in the collection
            // Create a snapshot to avoid collection modified exception during enumeration
            var existingSet = new HashSet<PluginListItem>(_filteredPlugins.ToList());
            foreach (var item in filtered)
            {
                if (!existingSet.Contains(item))
                {
                    _filteredPlugins.Add(item);
                }
            }
        }
        finally
        {
            _isApplyingFilter = false;
        }
    }

    public virtual void AddErrorMessage(string message, int maxMessages = 10)
    {
        // Ensure collection operations happen on UI thread
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => AddErrorMessage(message, maxMessages));
            return;
        }

        ErrorMessages.Add(message);

        if (ErrorMessages.Count > maxMessages)
        {
            ErrorMessages.RemoveAt(0);
        }
    }

    public virtual void AddInformationMessage(string message, int maxMessages = 10)
    {
        // Ensure collection operations happen on UI thread
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => AddInformationMessage(message, maxMessages));
            return;
        }

        InformationMessages.Add(message);

        if (InformationMessages.Count > maxMessages)
        {
            InformationMessages.RemoveAt(0);
        }
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    public void ResetProgress()
    {
        // Ensure collection operations happen on UI thread
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ResetProgress());
            return;
        }

        ProgressValue = 0;
        ProgressStatus = string.Empty;
        IsProcessing = false;
        ErrorMessages.Clear();
        InformationMessages.Clear();
    }

    public void UpdateProgress(string status, double? value = null)
    {
        // Ensure UI updates happen on UI thread
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => UpdateProgress(status, value));
            return;
        }

        ProgressStatus = status;
        if (value.HasValue)
        {
            ProgressValue = value.Value;
        }
    }
}
