using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FormID_Database_Manager.Models;

public partial class PluginListItem : ObservableObject, IDataErrorInfo
{
    [ObservableProperty] private string _name = string.Empty;

    [ObservableProperty] private bool _isSelected;

    /// <summary>
    ///     Gets the confirmed Plugin List membership version from which this presentation item was projected.
    /// </summary>
    public long MembershipVersion { get; init; }

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
}
