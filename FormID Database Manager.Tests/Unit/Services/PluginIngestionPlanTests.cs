using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.TestUtilities.Builders;
using Moq;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Records;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

/// <summary>
///     Covers the planning half of Plugin Ingestion, which answers what a Processing Run would do without doing any
///     of it: no Store session, no database, and no record enumeration.
/// </summary>
/// <remarks>
///     Issue #67, under parent #61. These scenarios generate their own Plugin files in a temporary directory and
///     supply their own load-order and overlay adapters, so none of them needs a real game installation (ADR-0002).
/// </remarks>
public sealed class PluginIngestionPlanTests : IDisposable
{
    private readonly List<string> _tempDirectories = [];

    /// <summary>
    ///     Verifies that planning prepares one load-order snapshot from the canonical Data directory, opens each
    ///     selected Plugin's overlay exactly once in selection order, releases every one of them, and enumerates no
    ///     records at all.
    /// </summary>
    [Fact]
    public async Task PlanAsync_SelectedPlugins_OpensAndReleasesEveryOverlayWithoutEnumeratingRecords()
    {
        var gameDirectory = CreateGameDirectory();
        await CreatePluginFileAsync(gameDirectory, "First.esp");
        await CreatePluginFileAsync(gameDirectory, "Second.esp");
        var loadOrderProvider = new RecordingLoadOrderProvider(
            new GameLoadOrderSnapshot(
                ["First.esp", "Second.esp"],
                [
                    new KeyedMasterStyle(ModKey.FromNameAndExtension("First.esp"), MasterStyle.Full),
                    new KeyedMasterStyle(ModKey.FromNameAndExtension("Second.esp"), MasterStyle.Full)
                ]));
        var overlayReader = new PlanningOverlayReader();
        IPluginIngestion sut = new PluginIngestion(loadOrderProvider, overlayReader, new EntryExtraction());

        var plan = await sut.PlanAsync(
            CreateRequest(gameDirectory, "First.esp", "Second.esp"),
            TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(gameDirectory, "Data"), loadOrderProvider.CapturedDataPath);
        Assert.Equal(1, loadOrderProvider.BuildSnapshotCallCount);
        Assert.True(loadOrderProvider.CapturedIncludeMasterFlagsLookup);
        Assert.All(overlayReader.CapturedReadParameters, parameters => Assert.NotNull(parameters.MasterFlagsLookup));
        Assert.Equal(["First.esp", "Second.esp"], overlayReader.OpenedPlugins);
        Assert.Equal(["First.esp", "Second.esp"], overlayReader.DisposedPlugins);
        Assert.Collection(
            plan.Plugins,
            planned => Assert.Equal("First.esp", Assert.IsType<PlannedPluginIngestion>(planned).PluginName),
            planned => Assert.Equal("Second.esp", Assert.IsType<PlannedPluginIngestion>(planned).PluginName));
    }

    /// <summary>
    ///     Verifies that a Plugin whose overlay contains no records is still planned as one that would be ingested.
    /// </summary>
    /// <remarks>
    ///     This is the deliberate gap between a plan and the run it predicts: the run would skip this Plugin for
    ///     producing zero FormID records, and the plan cannot say so without enumerating them, which is exactly the
    ///     work it refuses to do. The plan therefore has no case for that reason at all rather than guessing at one.
    /// </remarks>
    [Fact]
    public async Task PlanAsync_PluginWithNoRecords_StillReportsWouldIngestBecauseZeroRecordsIsNotPredictable()
    {
        var gameDirectory = CreateGameDirectory();
        await CreatePluginFileAsync(gameDirectory, "Empty.esp");
        IPluginIngestion sut = new PluginIngestion(
            new RecordingLoadOrderProvider(new GameLoadOrderSnapshot(["Empty.esp"])),
            new EmptyPluginOverlayReader(),
            new EntryExtraction());

        var plan = await sut.PlanAsync(
            CreateRequest(gameDirectory, "Empty.esp"),
            TestContext.Current.CancellationToken);

        Assert.Equal("Empty.esp", Assert.IsType<PlannedPluginIngestion>(Assert.Single(plan.Plugins)).PluginName);
    }

    /// <summary>
    ///     Verifies that the two predictable skips are reported per Plugin, in selection order, without opening an
    ///     overlay for either of them, and that the unavailable-file skip keeps the path the plan resolved.
    /// </summary>
    [Fact]
    public async Task PlanAsync_PredictableSkips_ReportsBothReasonsWithoutOpeningAnOverlay()
    {
        var gameDirectory = CreateGameDirectory();
        await CreatePluginFileAsync(gameDirectory, "Available.esp");
        var overlayReader = new PlanningOverlayReader();
        IPluginIngestion sut = new PluginIngestion(
            new RecordingLoadOrderProvider(new GameLoadOrderSnapshot(["Unavailable.esp", "Available.esp"])),
            overlayReader,
            new EntryExtraction());

        var plan = await sut.PlanAsync(
            CreateRequest(gameDirectory, "Absent.esp", "Unavailable.esp", "Available.esp"),
            TestContext.Current.CancellationToken);

        Assert.Equal(["Available.esp"], overlayReader.OpenedPlugins);
        Assert.Collection(
            plan.Plugins,
            planned =>
            {
                var skip = Assert.IsType<PlannedPluginSkip>(planned);
                Assert.Equal("Absent.esp", skip.PluginName);
                Assert.Equal(PlannedSkipReason.NotPresentInLoadOrder, skip.Reason);
                Assert.Null(skip.ResolvedPluginPath);
            },
            planned =>
            {
                var skip = Assert.IsType<PlannedPluginSkip>(planned);
                Assert.Equal("Unavailable.esp", skip.PluginName);
                Assert.Equal(PlannedSkipReason.PluginFileUnavailable, skip.Reason);
                Assert.Equal(Path.Combine(gameDirectory, "Data", "Unavailable.esp"), skip.ResolvedPluginPath);
            },
            planned => Assert.IsType<PlannedPluginIngestion>(planned));
    }

    /// <summary>
    ///     Verifies that a Plugin-specific overlay failure is reported as a planned failure and does not stop the plan,
    ///     matching how a real run reports the same failure as one Failed Plugin.
    /// </summary>
    [Fact]
    public async Task PlanAsync_OverlayOpeningFailure_ReportsAPlannedFailureAndKeepsPlanningLaterPlugins()
    {
        var gameDirectory = CreateGameDirectory();
        await CreatePluginFileAsync(gameDirectory, "Broken.esp");
        await CreatePluginFileAsync(gameDirectory, "Later.esp");
        var overlayReader = new PlanningOverlayReader
        {
            Failures =
            {
                ["Broken.esp"] = new PluginOverlayReadException(
                    "Unreadable header.",
                    new MalformedDataException("Unreadable header."))
            }
        };
        IPluginIngestion sut = new PluginIngestion(
            new RecordingLoadOrderProvider(new GameLoadOrderSnapshot(["Broken.esp", "Later.esp"])),
            overlayReader,
            new EntryExtraction());

        var plan = await sut.PlanAsync(
            CreateRequest(gameDirectory, "Broken.esp", "Later.esp"),
            TestContext.Current.CancellationToken);

        Assert.Equal(["Broken.esp", "Later.esp"], overlayReader.OpenedPlugins);
        Assert.Collection(
            plan.Plugins,
            planned =>
            {
                var failure = Assert.IsType<PlannedPluginFailure>(planned);
                Assert.Equal("Broken.esp", failure.PluginName);
                Assert.Equal(PluginReadPhase.OpeningPlugin, failure.Diagnostic.Phase);
                Assert.Equal("Unreadable header.", failure.Diagnostic.Message);
            },
            planned => Assert.Equal("Later.esp", Assert.IsType<PlannedPluginIngestion>(planned).PluginName));
    }

    /// <summary>
    ///     Verifies that a master the Data directory cannot supply fails the whole plan, naming that master, exactly as
    ///     it fails a whole run.
    /// </summary>
    /// <remarks>
    ///     ADR-0006 and issue #52. This is the point of a plan opening overlays at all: the user sees the failure before
    ///     committing to a run rather than after. The overlay opened for the earlier Plugin is asserted released,
    ///     because a plan that fails partway still owns everything it opened.
    /// </remarks>
    [Fact]
    public async Task PlanAsync_DeclaredMasterMissingFromTheLookup_FailsTheWholePlanAndReleasesEarlierOverlays()
    {
        var gameDirectory = CreateGameDirectory();
        await CreatePluginFileAsync(gameDirectory, "First.esp");
        await CreatePluginFileAsync(gameDirectory, "Patch.esp");
        await CreatePluginFileAsync(gameDirectory, "Never.esp");
        var failure = new MissingModException(
            ModKey.FromNameAndExtension("Starfield.esm"),
            "Mod was missing from load order when constructing the separate mod lists needed for FormID translation.");
        var overlayReader = new PlanningOverlayReader { Failures = { ["Patch.esp"] = failure } };
        IPluginIngestion sut = new PluginIngestion(
            new RecordingLoadOrderProvider(
                GameLoadOrderSnapshotFactory.CreateSnapshotWithoutAnyMasterOnDisk(
                    "First.esp",
                    "Patch.esp",
                    "Never.esp")),
            overlayReader,
            new EntryExtraction());

        var thrown = await Assert.ThrowsAsync<UnresolvableMasterException>(() => sut.PlanAsync(
            CreateRequest(gameDirectory, "First.esp", "Patch.esp", "Never.esp"),
            TestContext.Current.CancellationToken));

        Assert.Equal("Patch.esp", thrown.PluginName);
        Assert.Equal("Starfield.esm", thrown.MasterName);
        Assert.Same(failure, thrown.InnerException);
        Assert.Equal(["First.esp", "Patch.esp"], overlayReader.OpenedPlugins);
        Assert.Equal(["First.esp"], overlayReader.DisposedPlugins);
    }

    /// <summary>
    ///     Verifies that a plan cancelled before it starts consults no adapter at all.
    /// </summary>
    [Fact]
    public async Task PlanAsync_CancelledBeforePlanning_ThrowsWithoutPreparingLoadOrder()
    {
        var gameDirectory = CreateGameDirectory();
        await CreatePluginFileAsync(gameDirectory, "First.esp");
        var loadOrderProvider = new RecordingLoadOrderProvider(new GameLoadOrderSnapshot(["First.esp"]));
        var overlayReader = new PlanningOverlayReader();
        IPluginIngestion sut = new PluginIngestion(loadOrderProvider, overlayReader, new EntryExtraction());
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.PlanAsync(
            CreateRequest(gameDirectory, "First.esp"),
            cancellationTokenSource.Token));

        Assert.Equal(cancellationTokenSource.Token, thrown.CancellationToken);
        Assert.Equal(0, loadOrderProvider.BuildSnapshotCallCount);
        Assert.Empty(overlayReader.OpenedPlugins);
    }

    /// <summary>
    ///     Verifies that cancellation observed between selected Plugins stops the plan before the next overlay is
    ///     opened, and returns no plan at all rather than a partial one.
    /// </summary>
    [Fact]
    public async Task PlanAsync_CancelledBetweenSelectedPlugins_StopsBeforeTheNextOverlay()
    {
        var gameDirectory = CreateGameDirectory();
        await CreatePluginFileAsync(gameDirectory, "First.esp");
        await CreatePluginFileAsync(gameDirectory, "Second.esp");
        using var cancellationTokenSource = new CancellationTokenSource();
        var overlayReader = new PlanningOverlayReader
        {
            OnOpened = pluginName =>
            {
                if (string.Equals(pluginName, "First.esp", StringComparison.Ordinal))
                {
                    cancellationTokenSource.Cancel();
                }
            }
        };
        IPluginIngestion sut = new PluginIngestion(
            new RecordingLoadOrderProvider(new GameLoadOrderSnapshot(["First.esp", "Second.esp"])),
            overlayReader,
            new EntryExtraction());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.PlanAsync(
            CreateRequest(gameDirectory, "First.esp", "Second.esp"),
            cancellationTokenSource.Token));

        Assert.Equal(["First.esp"], overlayReader.OpenedPlugins);
        Assert.Equal(["First.esp"], overlayReader.DisposedPlugins);
    }

    /// <summary>
    ///     Verifies that planning never touches a FormID Record Store, which is what makes a dry run safe to run
    ///     against a database path the user has not chosen yet.
    /// </summary>
    /// <remarks>
    ///     The contract carries no Store parameter at all, so this asserts the shape rather than a call count: there is
    ///     no seam through which a plan could reach a Store to begin with.
    /// </remarks>
    [Fact]
    public void PlanAsync_ContractShape_TakesNoStoreSession()
    {
        var operation = typeof(IPluginIngestion).GetMethod(nameof(IPluginIngestion.PlanAsync));

        Assert.NotNull(operation);
        Assert.DoesNotContain(
            operation.GetParameters(),
            parameter => parameter.ParameterType == typeof(IFormIdRecordStoreSession));
    }

    public void Dispose()
    {
        foreach (var directory in _tempDirectories)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch
            {
                /* Test cleanup is best-effort; a generated Plugin file can be held briefly. */
            }
        }
    }

    private static SelectedPluginIngestionRequest CreateRequest(string gameDirectory, params string[] pluginNames)
    {
        return new SelectedPluginIngestionRequest(
            gameDirectory,
            GameRelease.SkyrimSE,
            pluginNames,
            UpdateMode.Append);
    }

    private string CreateGameDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"plugin_plan_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "Data"));
        _tempDirectories.Add(directory);
        return directory;
    }

    private static async Task CreatePluginFileAsync(string gameDirectory, string pluginName)
    {
        var pluginPath = Path.Combine(gameDirectory, "Data", pluginName);
        await File.WriteAllBytesAsync(pluginPath, [0x00], TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     An overlay adapter that records what a plan opened and released, and fails loudly if a plan ever enumerates
    ///     records — the one thing planning is defined not to do.
    /// </summary>
    private sealed class PlanningOverlayReader : IPluginOverlayReader
    {
        public List<string> OpenedPlugins { get; } = [];

        public List<string> DisposedPlugins { get; } = [];

        public List<BinaryReadParameters> CapturedReadParameters { get; } = [];

        /// <summary>
        ///     The failure to raise instead of returning an overlay, keyed by Plugin name.
        /// </summary>
        public Dictionary<string, Exception> Failures { get; } = new(StringComparer.Ordinal);

        /// <summary>
        ///     Runs after a Plugin has been recorded as opened, so a scenario can cancel mid-plan.
        /// </summary>
        public Action<string>? OnOpened { get; init; }

        public IModDisposeGetter ReadOverlay(
            string pluginPath,
            GameRelease gameRelease,
            BinaryReadParameters readParameters)
        {
            var pluginName = Path.GetFileName(pluginPath);
            OpenedPlugins.Add(pluginName);
            CapturedReadParameters.Add(readParameters);
            OnOpened?.Invoke(pluginName);
            if (Failures.TryGetValue(pluginName, out var failure))
            {
                throw failure;
            }

            var overlay = new Mock<IModDisposeGetter>();
            overlay
                .Setup(plugin => plugin.EnumerateMajorRecords())
                .Throws(new InvalidOperationException("A Processing Run plan must not enumerate records."));
            overlay.Setup(plugin => plugin.Dispose()).Callback(() => DisposedPlugins.Add(pluginName));
            return overlay.Object;
        }
    }

    /// <summary>
    ///     An overlay adapter whose Plugins open cleanly and contain no records at all.
    /// </summary>
    private sealed class EmptyPluginOverlayReader : IPluginOverlayReader
    {
        public IModDisposeGetter ReadOverlay(
            string pluginPath,
            GameRelease gameRelease,
            BinaryReadParameters readParameters)
        {
            var overlay = new Mock<IModDisposeGetter>();
            overlay.Setup(plugin => plugin.EnumerateMajorRecords()).Returns([]);
            return overlay.Object;
        }
    }

    private sealed class RecordingLoadOrderProvider(GameLoadOrderSnapshot snapshot) : IGameLoadOrderProvider
    {
        public int BuildSnapshotCallCount { get; private set; }

        public string CapturedDataPath { get; private set; } = null!;

        public bool CapturedIncludeMasterFlagsLookup { get; private set; }

        public GameLoadOrderSnapshot BuildSnapshot(
            GameRelease gameRelease,
            string dataPath,
            bool includeMasterFlagsLookup = false)
        {
            BuildSnapshotCallCount++;
            CapturedDataPath = dataPath;
            CapturedIncludeMasterFlagsLookup = includeMasterFlagsLookup;
            return snapshot;
        }
    }
}
