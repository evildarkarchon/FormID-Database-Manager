using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using FormID_Database_Manager.Models;
using Avalonia.Threading;

namespace FormID_Database_Manager.ViewModels;

public class MainWindowViewModel : INotifyPropertyChanged
{
    private string _gameDirectory = string.Empty;
    private string _databasePath = string.Empty;
    private string _formIdListPath = string.Empty;
    private string _detectedGame = string.Empty;
    private bool _isProcessing;
    private double _progressValue;
    private string _progressStatus = string.Empty;
    private ObservableCollection<PluginListItem> _plugins;
    private ObservableCollection<PluginListItem> _filteredPlugins = [];
    private string _pluginFilter = string.Empty;
    private ObservableCollection<string> _errorMessages = [];
    private ObservableCollection<string> _informationMessages = [];
    
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
        // Ensure filter operations happen on UI thread
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ApplyFilter());
            return;
        }

        if (string.IsNullOrWhiteSpace(_pluginFilter))
        {
            FilteredPlugins = new ObservableCollection<PluginListItem>(_plugins);
        }
        else
        {
            var filteredList = _plugins
                .Where(p => p.Name.Contains(_pluginFilter, StringComparison.OrdinalIgnoreCase))
                .ToList();
            FilteredPlugins = new ObservableCollection<PluginListItem>(filteredList);
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
