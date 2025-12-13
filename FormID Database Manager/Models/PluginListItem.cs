using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FormID_Database_Manager.Models;

public class PluginListItem : INotifyPropertyChanged, IDataErrorInfo
{
    private bool _isSelected;
    private string _name = string.Empty;

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
    // Note: IDataErrorInfo predates nullable reference types and expects null/"" for no error.
    // Using string.Empty for modern nullable semantics consistency.
    public string Error => string.Empty;

    public string this[string columnName]
    {
        get
        {
            return columnName switch
            {
                nameof(Name) => string.IsNullOrWhiteSpace(Name) ? "Name cannot be empty" : string.Empty,
                _ => string.Empty
            };
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
