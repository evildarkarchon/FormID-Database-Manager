using Mutagen.Bethesda;

namespace FormID_Database_Manager.Services;

internal interface IPluginListRefresh
{
    Task<PluginListRefreshResult> RefreshAsync(
        PluginListRefreshRequest request,
        IProgress<PluginListRefreshProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

internal enum AdvancedMode
{
    Off,
    On
}

internal sealed record PluginListRefreshRequest(
    string GameDirectory,
    GameRelease GameRelease,
    AdvancedMode AdvancedMode);

internal enum PluginListRefreshStatus
{
    Completed,
    Stale,
    Cancelled,
    Failed
}

internal sealed record PluginListEntry(string Name);

internal readonly record struct PluginListRefreshProgress(int ScannedCount, int TotalCount);

internal sealed record PluginListRefreshResult(
    PluginListRefreshStatus Status,
    IReadOnlyList<PluginListEntry> Plugins,
    int LoadedCount,
    string? ErrorMessage = null)
{
    public static PluginListRefreshResult Completed(IReadOnlyList<PluginListEntry> plugins)
    {
        return new PluginListRefreshResult(PluginListRefreshStatus.Completed, plugins, plugins.Count);
    }

    public static PluginListRefreshResult Stale()
    {
        return new PluginListRefreshResult(PluginListRefreshStatus.Stale, [], 0);
    }

    public static PluginListRefreshResult Cancelled()
    {
        return new PluginListRefreshResult(PluginListRefreshStatus.Cancelled, [], 0);
    }

    public static PluginListRefreshResult Failed(string message)
    {
        return new PluginListRefreshResult(PluginListRefreshStatus.Failed, [], 0, message);
    }
}

/// <summary>
///     Builds a Plugin List for a selected GameRelease and directory behind a UI-neutral interface.
/// </summary>
internal sealed class PluginListRefresh(
    GameDetectionService gameDetectionService,
    IGameLoadOrderProvider? loadOrderProvider = null)
    : IPluginListRefresh
{
    private readonly GameDetectionService _gameDetectionService =
        gameDetectionService ?? throw new ArgumentNullException(nameof(gameDetectionService));
    private readonly IGameLoadOrderProvider _loadOrderProvider = loadOrderProvider ?? new GameLoadOrderProvider();
    private int _refreshVersion;

    public Task<PluginListRefreshResult> RefreshAsync(
        PluginListRefreshRequest request,
        IProgress<PluginListRefreshProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.GameDirectory);

        var refreshVersion = Interlocked.Increment(ref _refreshVersion);
        return Task.Run(() => RefreshCore(request, progress, refreshVersion, cancellationToken));
    }

    private PluginListRefreshResult RefreshCore(
        PluginListRefreshRequest request,
        IProgress<PluginListRefreshProgress>? progress,
        int refreshVersion,
        CancellationToken cancellationToken)
    {
        bool IsLatestRefresh()
        {
            return refreshVersion == Volatile.Read(ref _refreshVersion);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var dataPath = GameReleaseHelper.ResolveDataPath(request.GameDirectory);
            var loadOrder = _loadOrderProvider
                .BuildSnapshot(request.GameRelease, dataPath)
                .ListedPluginNames;

            if (!IsLatestRefresh())
            {
                return PluginListRefreshResult.Stale();
            }

            ReportProgress(progress, 0, loadOrder.Count);

            var basePluginSet = new HashSet<string>(
                _gameDetectionService.GetBaseGamePlugins(request.GameRelease),
                StringComparer.OrdinalIgnoreCase);
            var plugins = new List<PluginListEntry>();
            var addedPlugins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < loadOrder.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!IsLatestRefresh())
                {
                    return PluginListRefreshResult.Stale();
                }

                var pluginName = loadOrder[i];

                if (addedPlugins.Contains(pluginName))
                {
                    ReportProgress(progress, i + 1, loadOrder.Count);
                    continue;
                }

                if (request.AdvancedMode == AdvancedMode.Off && basePluginSet.Contains(pluginName))
                {
                    ReportProgress(progress, i + 1, loadOrder.Count);
                    continue;
                }

                if (!File.Exists(Path.Combine(dataPath, pluginName)))
                {
                    ReportProgress(progress, i + 1, loadOrder.Count);
                    continue;
                }

                plugins.Add(new PluginListEntry(pluginName));
                addedPlugins.Add(pluginName);
                ReportProgress(progress, i + 1, loadOrder.Count);
            }

            return IsLatestRefresh()
                ? PluginListRefreshResult.Completed(plugins)
                : PluginListRefreshResult.Stale();
        }
        catch (OperationCanceledException)
        {
            return PluginListRefreshResult.Cancelled();
        }
        catch (Exception ex)
        {
            return PluginListRefreshResult.Failed(ex.Message);
        }
    }

    private static void ReportProgress(
        IProgress<PluginListRefreshProgress>? progress,
        int scannedCount,
        int totalCount)
    {
        if (progress is null)
        {
            return;
        }

        if (scannedCount == 0 || scannedCount % 10 == 0 || scannedCount == totalCount)
        {
            progress.Report(new PluginListRefreshProgress(scannedCount, totalCount));
        }
    }
}
