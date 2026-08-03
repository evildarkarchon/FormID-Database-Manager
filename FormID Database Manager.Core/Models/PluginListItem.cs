using CommunityToolkit.Mvvm.ComponentModel;

namespace FormID_Database_Manager.Models;

public partial class PluginListItem : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;

    [ObservableProperty] private bool _isSelected;

    /// <summary>
    ///     Gets the confirmed Plugin List membership version from which this presentation item was projected.
    /// </summary>
    public long MembershipVersion { get; init; }
}
