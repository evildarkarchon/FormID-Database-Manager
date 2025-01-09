using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using FormID_Database_Manager.Models;

namespace FormID_Database_Manager.ViewModels;

public class MainWindowViewModel : INotifyPropertyChanged
{
    private string _gameDirectory = string.Empty;
    private string _databasePath = string.Empty;
    private string _detectedGame = string.Empty;
    private bool _isProcessing;
    private ObservableCollection<PluginListItem> _plugins = new();

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

    public ObservableCollection<PluginListItem> Plugins
    {
        get => _plugins;
        set => SetProperty(ref _plugins, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

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
}