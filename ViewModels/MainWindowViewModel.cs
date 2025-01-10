using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using FormID_Database_Manager.Models;

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
    private ObservableCollection<PluginListItem> _plugins = new();
    private ObservableCollection<string> _errorMessages = new();
    private ObservableCollection<string> _informationMessages = new();

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
        set => SetProperty(ref _plugins, value);
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

    // Method to add error messages with optional limit to prevent overflow
    public void AddErrorMessage(string message, int maxMessages = 10)
    {
        ErrorMessages.Add(message);

        // Optionally limit the number of error messages
        if (ErrorMessages.Count > maxMessages)
        {
            ErrorMessages.RemoveAt(0);
        }
    }

    // Method to add information messages
    public void AddInformationMessage(string message, int maxMessages = 10)
    {
        InformationMessages.Add(message);

        // Optionally limit the number of information messages
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
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    public void ResetProgress()
    {
        ProgressValue = 0;
        ProgressStatus = string.Empty;
        IsProcessing = false;
        ErrorMessages.Clear();
        InformationMessages.Clear();
    }

    public void UpdateProgress(string status, double? value = null)
    {
        ProgressStatus = status;
        if (value.HasValue)
        {
            ProgressValue = value.Value;
        }
    }
}