using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FormID_Database_Manager.Models;

public class PluginListItem : INotifyPropertyChanged, IDataErrorInfo
{
    public string Name
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            OnPropertyChanged();
        }
    } = string.Empty;

    public bool IsSelected
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            OnPropertyChanged();
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
