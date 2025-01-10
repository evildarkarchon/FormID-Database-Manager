using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FormID_Database_Manager.Models;

public class PluginListItem : INotifyPropertyChanged, IDataErrorInfo
{
    private string _name = string.Empty;
    private bool _isSelected;

    public string Name
    {
        get => _name;
        set
        {
            if (_name != value)
            {
                _name = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    // IDataErrorInfo implementation to prevent error states
    public string Error => null!;

    public string this[string columnName]
    {
        get
        {
            switch (columnName)
            {
                case nameof(Name):
                    return (string.IsNullOrWhiteSpace(Name) ? "Name cannot be empty" : null) ?? string.Empty;
                default:
                    return null!;
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}