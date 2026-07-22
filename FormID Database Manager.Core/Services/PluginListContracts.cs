using System.Collections.Immutable;

namespace FormID_Database_Manager.Services;

internal enum AdvancedMode
{
    Off,
    On
}

/// <summary>
///     Describes one immutable Plugin entry in Plugin List order.
/// </summary>
/// <param name="Name">The Plugin name using the casing reported by discovery.</param>
internal sealed record PluginListEntry(string Name);

/// <summary>
///     Captures one confirmed membership and selection snapshot for a Plugin List Source.
/// </summary>
internal sealed record ConfirmedPluginList(
    long MembershipVersion,
    PluginListSource Source,
    AdvancedMode AdvancedMode,
    ImmutableArray<PluginListEntry> Entries,
    ImmutableArray<string> SelectedPluginNames);

/// <summary>
///     Describes UI-neutral Plugin List activity.
/// </summary>
internal abstract record PluginListActivity;

internal sealed record PluginListNoSourceActivity : PluginListActivity;

internal sealed record PluginListRefreshingActivity(
    PluginListSource Source,
    int ScannedCount,
    int TotalCount) : PluginListActivity;

internal sealed record PluginListReadyActivity(
    PluginListSource Source,
    long MembershipVersion) : PluginListActivity;

internal sealed record PluginListFailedActivity(
    PluginListSource Source,
    string ErrorMessage) : PluginListActivity;

internal sealed record PluginListCancelledActivity(PluginListSource Source) : PluginListActivity;

/// <summary>
///     Provides one immutable view of Plugin List membership, selection, and current activity.
/// </summary>
internal sealed record PluginListState(
    long StateRevision,
    ConfirmedPluginList? Confirmed,
    PluginListActivity Activity)
{
    public static PluginListState Initial { get; } = new(0, null, new PluginListNoSourceActivity());
}

/// <summary>
///     Carries versioned intent to change one Plugin or the complete confirmed Plugin List.
/// </summary>
/// <param name="MembershipVersion">The membership version displayed when the intent originated.</param>
/// <param name="IsSelected">The desired selection state.</param>
internal abstract record PluginSelectionIntent(long MembershipVersion, bool IsSelected);

internal sealed record PluginSelectionByNameIntent(
    long MembershipVersion,
    string PluginName,
    bool IsSelected) : PluginSelectionIntent(MembershipVersion, IsSelected);

internal sealed record PluginSelectionForAllIntent(
    long MembershipVersion,
    bool IsSelected) : PluginSelectionIntent(MembershipVersion, IsSelected);

internal readonly record struct PluginListDiscoveryProgress(int ScannedCount, int TotalCount);

/// <summary>
///     Represents either discovered available Plugin names or one normalized expected local failure.
/// </summary>
internal abstract record PluginListDiscoveryResult
{
    /// <summary>
    ///     Creates a successful discovery result with an immutable copy of the ordered available names.
    /// </summary>
    /// <param name="pluginNames">The available Plugin names in raw load-order order.</param>
    /// <returns>A successful immutable discovery result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pluginNames" /> is null.</exception>
    public static PluginListDiscoveryResult Completed(IEnumerable<string> pluginNames)
    {
        ArgumentNullException.ThrowIfNull(pluginNames);
        return new PluginListDiscoveryCompleted(pluginNames.ToImmutableArray());
    }

    /// <summary>
    ///     Creates a failed discovery result for an expected local environment error.
    /// </summary>
    /// <param name="errorMessage">The UI-neutral local failure detail.</param>
    /// <returns>A failed discovery result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="errorMessage" /> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="errorMessage" /> is empty or whitespace.</exception>
    public static PluginListDiscoveryResult Failed(string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new PluginListDiscoveryFailed(errorMessage);
    }
}

internal sealed record PluginListDiscoveryCompleted(
    ImmutableArray<string> PluginNames) : PluginListDiscoveryResult;

internal sealed record PluginListDiscoveryFailed(string ErrorMessage) : PluginListDiscoveryResult;

/// <summary>
///     Supplies ordered, available Plugin names without owning Plugin List membership rules.
/// </summary>
internal interface IPluginListDiscovery
{
    /// <summary>
    ///     Discovers available Plugin names for one normalized source and reports raw scan counts.
    /// </summary>
    /// <param name="source">The normalized source whose load order and files are inspected.</param>
    /// <param name="progress">An optional synchronous sink for raw scanned and total counts.</param>
    /// <param name="cancellationToken">Cancels the caller's discovery request.</param>
    /// <returns>A task containing available names or an expected local failure fact.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source" /> is null.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> is cancelled.</exception>
    /// <remarks>
    ///     Programming defects, fatal failures, and exceptions thrown by <paramref name="progress" /> are not normalized
    ///     and propagate to the caller.
    /// </remarks>
    Task<PluginListDiscoveryResult> DiscoverAsync(
        PluginListSource source,
        IProgress<PluginListDiscoveryProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
