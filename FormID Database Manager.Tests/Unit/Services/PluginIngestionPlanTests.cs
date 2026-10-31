using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.Tests.Fakes;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

/// <summary>
///     Covers the planning half of Plugin Ingestion, which answers what a Processing Run would do without doing any
///     of it: no Store session, no database, and no record enumeration.
/// </summary>
/// <remarks>
///     Issue #67, under parent #61. These scenarios generate their own Plugin files in a temporary directory and
///     supply their own load-order adapter and Plugin reader, so none of them needs a real game installation (ADR-0002).
/// </remarks>
public sealed class PluginIngestionPlanTests : IDisposable
{
    private readonly List<string> _tempDirectories = [];

    /// <summary>
    ///     Verifies that planning reads the Data directory from the request's Plugin List Source, prepares the complete
    ///     ordered selection once, and passes each ready case through the Plugin read seam without reconstructing its
    ///     path or capability.
    /// </summary>
    [Fact]
    public async Task PlanAsync_PreparedReadyCases_PreparesOnceAndPassesEachCaseIntact()
    {
        var gameDirectory = CreateGameDirectory();
        var dataDirectory = Path.Combine(gameDirectory, "Data");
        var capability = new PluginReadCapability(new object());
        var first = new SelectedPluginReady(
            "FIRST.esp",
            Path.Combine(dataDirectory, "First-Resolved.esp"),
            capability);
        var second = new SelectedPluginReady(
            "second.ESP",
            Path.Combine(dataDirectory, "Second-Resolved.esp"),
            capability);
        var gameLoadOrders = new RecordingPreparedGameLoadOrders([first, second]);
        var pluginReader = new ScriptedPluginReader();
        IPluginIngestion sut = new PluginIngestion(gameLoadOrders, pluginReader);

        var request = CreateRequest(gameDirectory, "FIRST.esp", "second.ESP");

        var plan = await sut.PlanAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(1, gameLoadOrders.PrepareCallCount);
        Assert.Equal(request.Source.DataDirectory, gameLoadOrders.CapturedCanonicalDataDirectory, StringComparer.Ordinal);
        Assert.Equal(["FIRST.esp", "second.ESP"], gameLoadOrders.CapturedSelection);
        Assert.Collection(
            pluginReader.OpenedCases,
            opened => Assert.Same(first, opened),
            opened => Assert.Same(second, opened));
        Assert.Equal(["FIRST.esp", "second.ESP"], pluginReader.DisposedPlugins);
        Assert.All(plan.Plugins, planned => Assert.IsType<PlannedPluginIngestion>(planned));
    }

    /// <summary>
    ///     Verifies that planning prepares the selected set from the canonical Data directory, opens each
    ///     selected Plugin exactly once in selection order, releases every one of them, and reads no records at all.
    /// </summary>
    [Fact]
    public async Task PlanAsync_SelectedPlugins_OpensAndReleasesEveryPluginWithoutReadingRecords()
    {
        var gameDirectory = CreateGameDirectory();
        var gameLoadOrders = new RecordingPreparedGameLoadOrders(CreateReadyPlugins(
            gameDirectory,
            "First.esp",
            "Second.esp"));
        var pluginReader = new ScriptedPluginReader { RejectsReading = true };
        IPluginIngestion sut = new PluginIngestion(gameLoadOrders, pluginReader);

        var request = CreateRequest(gameDirectory, "First.esp", "Second.esp");

        var plan = await sut.PlanAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(request.Source.DataDirectory, gameLoadOrders.CapturedCanonicalDataDirectory);
        Assert.Equal(1, gameLoadOrders.PrepareCallCount);
        Assert.Equal(["First.esp", "Second.esp"], pluginReader.OpenedPlugins);
        Assert.Equal(["First.esp", "Second.esp"], pluginReader.DisposedPlugins);
        Assert.Collection(
            plan.Plugins,
            planned => Assert.Equal("First.esp", Assert.IsType<PlannedPluginIngestion>(planned).PluginName),
            planned => Assert.Equal("Second.esp", Assert.IsType<PlannedPluginIngestion>(planned).PluginName));
    }

    /// <summary>
    ///     Verifies that a Plugin that contains no records is still planned as one that would be ingested.
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
        IPluginIngestion sut = new PluginIngestion(
            new RecordingPreparedGameLoadOrders(CreateReadyPlugins(gameDirectory, "Empty.esp")),
            new ScriptedPluginReader { EmptyPlugins = { "Empty.esp" } });

        var plan = await sut.PlanAsync(
            CreateRequest(gameDirectory, "Empty.esp"),
            TestContext.Current.CancellationToken);

        Assert.Equal("Empty.esp", Assert.IsType<PlannedPluginIngestion>(Assert.Single(plan.Plugins)).PluginName);
    }

    /// <summary>
    ///     Verifies that the two predictable skips are reported per Plugin, in selection order, without opening an
    ///     Plugin for either of them, and that the unavailable-file skip keeps the path the plan resolved.
    /// </summary>
    [Fact]
    public async Task PlanAsync_PredictableSkips_ReportsBothReasonsWithoutOpeningAPlugin()
    {
        var gameDirectory = CreateGameDirectory();
        var dataDirectory = Path.Combine(gameDirectory, "Data");
        var pluginReader = new ScriptedPluginReader { RejectsReading = true };
        IPluginIngestion sut = new PluginIngestion(
            new RecordingPreparedGameLoadOrders(
            [
                new SelectedPluginNotListed("Absent.esp"),
                new SelectedPluginFileUnavailable(
                    "Unavailable.esp",
                    Path.Combine(dataDirectory, "Unavailable.esp")),
                new SelectedPluginReady(
                    "Available.esp",
                    Path.Combine(dataDirectory, "Available.esp"),
                    new PluginReadCapability(new object()))
            ]),
            pluginReader);

        var plan = await sut.PlanAsync(
            CreateRequest(gameDirectory, "Absent.esp", "Unavailable.esp", "Available.esp"),
            TestContext.Current.CancellationToken);

        Assert.Equal(["Available.esp"], pluginReader.OpenedPlugins);
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
    ///     Verifies that a Plugin-specific opening failure is reported as a planned failure and does not stop the plan,
    ///     matching how a real run reports the same failure as one Failed Plugin.
    /// </summary>
    [Fact]
    public async Task PlanAsync_OpeningFailure_ReportsAPlannedFailureAndKeepsPlanningLaterPlugins()
    {
        var gameDirectory = CreateGameDirectory();
        var pluginReader = new ScriptedPluginReader
        {
            RejectsReading = true,
            OpeningFailures =
            {
                ["Broken.esp"] = new PluginReadException(PluginReadPhase.OpeningPlugin, "Unreadable header.")
            }
        };
        IPluginIngestion sut = new PluginIngestion(
            new RecordingPreparedGameLoadOrders(CreateReadyPlugins(gameDirectory, "Broken.esp", "Later.esp")),
            pluginReader);

        var plan = await sut.PlanAsync(
            CreateRequest(gameDirectory, "Broken.esp", "Later.esp"),
            TestContext.Current.CancellationToken);

        Assert.Equal(["Broken.esp", "Later.esp"], pluginReader.OpenedPlugins);
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
    ///     Verifies that a master the Data directory cannot supply fails the whole plan, unchanged, exactly as it fails
    ///     a whole run.
    /// </summary>
    /// <remarks>
    ///     ADR-0006 and issue #52. This is the point of a plan opening Plugins at all: the user sees the failure before
    ///     committing to a run rather than after. The Plugin opened earlier is asserted released, because a plan that
    ///     fails partway still owns everything it opened, and the failing Plugin has nothing opened to release. Which
    ///     Mutagen failures become an Unresolvable Master, and which master each names, is the Plugin reader's
    ///     classification since ADR-0015, and is pinned in <see cref="MutagenPluginReaderTests" /> and
    ///     <see cref="PluginOverlayConstructionTests" />.
    /// </remarks>
    [Fact]
    public async Task PlanAsync_UnresolvableMasterWhileOpening_FailsTheWholePlanAndReleasesEarlierPlugins()
    {
        var gameDirectory = CreateGameDirectory();
        var failure = new UnresolvableMasterException("Patch.esp", "Starfield.esm");
        var pluginReader = new ScriptedPluginReader
        {
            RejectsReading = true,
            OpeningFailures = { ["Patch.esp"] = failure }
        };
        IPluginIngestion sut = new PluginIngestion(
            new RecordingPreparedGameLoadOrders(CreateReadyPlugins(
                gameDirectory,
                "First.esp",
                "Patch.esp",
                "Never.esp")),
            pluginReader);

        var thrown = await Assert.ThrowsAsync<UnresolvableMasterException>(() => sut.PlanAsync(
            CreateRequest(gameDirectory, "First.esp", "Patch.esp", "Never.esp"),
            TestContext.Current.CancellationToken));

        Assert.Same(failure, thrown);
        Assert.Equal(["First.esp", "Patch.esp"], pluginReader.OpenedPlugins);
        Assert.Equal(["First.esp"], pluginReader.DisposedPlugins);
    }

    /// <summary>
    ///     Verifies that an opaque capability from an incompatible adapter remains a composition failure and escapes
    ///     planning before any filesystem access.
    /// </summary>
    [Fact]
    public async Task PlanAsync_IncompatibleReadCapability_PropagatesCompositionFailure()
    {
        var gameDirectory = CreateGameDirectory();
        var readyPlugin = new SelectedPluginReady(
            "Mismatch.esp",
            Path.Combine(gameDirectory, "does-not-exist", "Mismatch.esp"),
            new PluginReadCapability(new object()));
        IPluginIngestion sut = new PluginIngestion(
            new RecordingPreparedGameLoadOrders([readyPlugin]),
            new MutagenPluginReader());

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.PlanAsync(
            CreateRequest(gameDirectory, "Mismatch.esp"),
            TestContext.Current.CancellationToken));

        Assert.Contains("different adapter", thrown.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Verifies that a Plugin cleanup failure propagates when planning has no active failure or cancellation to
    ///     preserve.
    /// </summary>
    [Fact]
    public async Task PlanAsync_PluginDisposalFailure_PropagatesStandaloneFailure()
    {
        var gameDirectory = CreateGameDirectory();
        var disposalFailure = new InvalidOperationException("Plugin disposal failed.");
        var pluginReader = new ScriptedPluginReader
        {
            RejectsReading = true,
            DisposalFailures = { ["First.esp"] = disposalFailure }
        };
        IPluginIngestion sut = new PluginIngestion(
            new RecordingPreparedGameLoadOrders(CreateReadyPlugins(gameDirectory, "First.esp")),
            pluginReader);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.PlanAsync(
            CreateRequest(gameDirectory, "First.esp"),
            TestContext.Current.CancellationToken));

        Assert.Same(disposalFailure, thrown);
        Assert.Equal(["First.esp"], pluginReader.DisposedPlugins);
    }

    /// <summary>
    ///     Verifies that a plan cancelled before it starts consults no adapter at all.
    /// </summary>
    [Fact]
    public async Task PlanAsync_CancelledBeforePlanning_ThrowsWithoutPreparingLoadOrder()
    {
        var gameDirectory = CreateGameDirectory();
        var gameLoadOrders = new RecordingPreparedGameLoadOrders(CreateReadyPlugins(gameDirectory, "First.esp"));
        var pluginReader = new ScriptedPluginReader { RejectsReading = true };
        IPluginIngestion sut = new PluginIngestion(gameLoadOrders, pluginReader);
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.PlanAsync(
            CreateRequest(gameDirectory, "First.esp"),
            cancellationTokenSource.Token));

        Assert.Equal(cancellationTokenSource.Token, thrown.CancellationToken);
        Assert.Equal(0, gameLoadOrders.PrepareCallCount);
        Assert.Empty(pluginReader.OpenedPlugins);
    }

    /// <summary>
    ///     Verifies that cancellation observed between selected Plugins stops the plan before the next Plugin is
    ///     opened, and returns no plan at all rather than a partial one.
    /// </summary>
    [Fact]
    public async Task PlanAsync_CancelledBetweenSelectedPlugins_StopsBeforeTheNextOpening()
    {
        var gameDirectory = CreateGameDirectory();
        using var cancellationTokenSource = new CancellationTokenSource();
        var pluginReader = new ScriptedPluginReader
        {
            RejectsReading = true,
            OnOpened = pluginName =>
            {
                if (string.Equals(pluginName, "First.esp", StringComparison.Ordinal))
                {
                    cancellationTokenSource.Cancel();
                }
            }
        };
        IPluginIngestion sut = new PluginIngestion(
            new RecordingPreparedGameLoadOrders(CreateReadyPlugins(gameDirectory, "First.esp", "Second.esp")),
            pluginReader);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.PlanAsync(
            CreateRequest(gameDirectory, "First.esp", "Second.esp"),
            cancellationTokenSource.Token));

        Assert.Equal(["First.esp"], pluginReader.OpenedPlugins);
        Assert.Equal(["First.esp"], pluginReader.DisposedPlugins);
    }

    /// <summary>
    ///     Verifies that cancellation requested during final Plugin cleanup wins the completion race even when that
    ///     same cleanup raises a standalone failure.
    /// </summary>
    [Fact]
    public async Task PlanAsync_FinalPluginDisposalCancelsAndFails_PreservesCancellation()
    {
        var gameDirectory = CreateGameDirectory();
        using var cancellationTokenSource = new CancellationTokenSource();
        var pluginReader = new ScriptedPluginReader
        {
            RejectsReading = true,
            OnDisposed = _ => cancellationTokenSource.Cancel(),
            DisposalFailures =
            {
                ["First.esp"] = new InvalidOperationException("Plugin disposal failed.")
            }
        };
        IPluginIngestion sut = new PluginIngestion(
            new RecordingPreparedGameLoadOrders(CreateReadyPlugins(gameDirectory, "First.esp")),
            pluginReader);

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.PlanAsync(
            CreateRequest(gameDirectory, "First.esp"),
            cancellationTokenSource.Token));

        Assert.Equal(cancellationTokenSource.Token, thrown.CancellationToken);
        Assert.Equal(["First.esp"], pluginReader.DisposedPlugins);
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

    private static PluginProcessingRunRequest CreateRequest(string gameDirectory, params string[] pluginNames)
    {
        return new PluginProcessingRunRequest(
            PluginListSource.Create(GameRelease.SkyrimSE, gameDirectory),
            string.Empty,
            pluginNames,
            UpdateMode.Append,
            dryRun: true);
    }

    private string CreateGameDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"plugin_plan_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "Data"));
        _tempDirectories.Add(directory);
        return directory;
    }

    private static ImmutableArray<PreparedSelectedPlugin> CreateReadyPlugins(
        string gameDirectory,
        params string[] pluginNames)
    {
        var capability = new PluginReadCapability(new object());
        return pluginNames
            .Select(pluginName => (PreparedSelectedPlugin)new SelectedPluginReady(
                pluginName,
                Path.Combine(gameDirectory, "Data", pluginName),
                capability))
            .ToImmutableArray();
    }
}
