using CommunityToolkit.Mvvm.ComponentModel;

namespace FormID_Database_Manager.Models;

public partial class PluginListItem : ObservableObject
{
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string _name = string.Empty;

    /// <summary>
    ///     Gets the confirmed Plugin List membership version from which this presentation item was projected.
    /// </summary>
    public long MembershipVersion { get; init; }
}
