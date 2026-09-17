using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

public sealed class PluginIngestionContractTests
{
    /// <summary>
    ///     Verifies the two operations Plugin Ingestion offers for one captured selection: doing the work, and saying
    ///     what doing it would come to.
    /// </summary>
    /// <remarks>
    ///     Issue #67 added the second. The parameter lists are pinned because their difference is the contract: doing
    ///     the work takes the Plugin-write Store role and reports progress, while planning takes neither — a plan opens
    ///     no database at all, and its output is terminal rather than transient.
    /// </remarks>
    [Fact]
    public void IPluginIngestion_TypeDefinition_IsInternalInterfaceWithOneIngestionAndOnePlanningOperation()
    {
        var interfaceType = typeof(IPluginIngestion);
        var pluginWriterType = typeof(IPluginFormIdRecordWriter);
        var ingestion = Assert.Single(interfaceType.GetMethods(), method => method.Name == "IngestAsync");
        var planning = Assert.Single(interfaceType.GetMethods(), method => method.Name == "PlanAsync");

        Assert.True(interfaceType.IsInterface);
        Assert.True(interfaceType.IsNotPublic);
        Assert.Equal(2, interfaceType.GetMethods().Length);
        Assert.Equal(typeof(Task<PluginIngestionReport>), ingestion.ReturnType);
        Assert.Collection(
            ingestion.GetParameters(),
            parameter => Assert.Equal(typeof(SelectedPluginIngestionRequest), parameter.ParameterType),
            parameter => Assert.Equal(pluginWriterType, parameter.ParameterType),
            parameter =>
            {
                Assert.Equal(typeof(IProgress<PluginIngestionProgress>), parameter.ParameterType);
                Assert.True(parameter.HasDefaultValue);
                Assert.Null(parameter.DefaultValue);
            },
            parameter =>
            {
                Assert.Equal(typeof(CancellationToken), parameter.ParameterType);
                Assert.True(parameter.HasDefaultValue);
            });

        Assert.Equal(typeof(Task<PluginIngestionPlan>), planning.ReturnType);
        Assert.Collection(
            planning.GetParameters(),
            parameter => Assert.Equal(typeof(PluginProcessingRunRequest), parameter.ParameterType),
            parameter =>
            {
                Assert.Equal(typeof(CancellationToken), parameter.ParameterType);
                Assert.True(parameter.HasDefaultValue);
            });
    }

    /// <summary>
    ///     Verifies Plugin Ingestion's consumer-owned Store role exposes one atomic Plugin write without carrying Store
    ///     lifetime or maintenance operations across the seam.
    /// </summary>
    [Fact]
    public void IPluginFormIdRecordWriter_TypeDefinition_IsInternalSingleOperationRoleInheritedByStoreSession()
    {
        var writerType = typeof(IPluginFormIdRecordWriter);
        var storeSessionType = typeof(IFormIdRecordStoreSession);
        var writeOperation = Assert.Single(writerType.GetMethods());

        Assert.True(writerType.IsInterface);
        Assert.True(writerType.IsNotPublic);
        Assert.Equal("WritePluginAsync", writeOperation.Name);
        Assert.Equal(typeof(Task<FormIdPluginWriteResult>), writeOperation.ReturnType);
        Assert.Collection(
            writeOperation.GetParameters(),
            parameter => Assert.Equal(typeof(string), parameter.ParameterType),
            parameter => Assert.Equal(typeof(IEnumerable<FormIdRecord>), parameter.ParameterType),
            parameter => Assert.Equal(typeof(UpdateMode), parameter.ParameterType),
            parameter =>
            {
                Assert.Equal(typeof(CancellationToken), parameter.ParameterType);
                Assert.True(parameter.HasDefaultValue);
            });
        Assert.False(typeof(IAsyncDisposable).IsAssignableFrom(writerType));
        Assert.Contains(writerType, storeSessionType.GetInterfaces());
        Assert.Contains(typeof(IAsyncDisposable), storeSessionType.GetInterfaces());
        Assert.DoesNotContain(
            storeSessionType.GetMethods(System.Reflection.BindingFlags.Instance |
                                        System.Reflection.BindingFlags.Public |
                                        System.Reflection.BindingFlags.DeclaredOnly),
            method => method.Name == "WritePluginAsync");
    }

    /// <summary>
    ///     Verifies a plan's skip reasons are exactly the two a plan can predict without enumerating records.
    /// </summary>
    /// <remarks>
    ///     The run's third reason, zero FormID records, is deliberately absent: it cannot be known without doing the
    ///     enumeration a dry run refuses to do, so a plan cannot honestly represent it (issue #67).
    /// </remarks>
    [Fact]
    public void PlannedSkipReason_Values_AreOnlyTheTwoPredictableOnes()
    {
        Assert.Equal(
            [PlannedSkipReason.NotPresentInLoadOrder, PlannedSkipReason.PluginFileUnavailable],
            Enum.GetValues<PlannedSkipReason>());
    }

    /// <summary>
    ///     Verifies a planned unavailable-file skip keeps the resolved path, and that no other planned reason may carry
    ///     one — the same invariant the run's own Skipped Plugin enforces.
    /// </summary>
    [Fact]
    public void PlannedPluginSkip_ResolvedPath_IsRequiredForAnUnavailableFileAndRejectedOtherwise()
    {
        var pluginPath = Path.Combine(Path.GetTempPath(), "Skyrim", "Data", "Missing.esp");

        var skip = new PlannedPluginSkip("Missing.esp", PlannedSkipReason.PluginFileUnavailable, pluginPath);
        var missingPath = Assert.Throws<ArgumentNullException>(() =>
            new PlannedPluginSkip("Missing.esp", PlannedSkipReason.PluginFileUnavailable));
        var unwantedPath = Assert.Throws<ArgumentException>(() =>
            new PlannedPluginSkip("Absent.esp", PlannedSkipReason.NotPresentInLoadOrder, pluginPath));

        Assert.Equal(pluginPath, skip.ResolvedPluginPath);
        Assert.Equal("resolvedPluginPath", missingPath.ParamName);
        Assert.Equal("resolvedPluginPath", unwantedPath.ParamName);
    }

    /// <summary>
    ///     Verifies a plan preserves its planned outcomes in order after the source collection is mutated.
    /// </summary>
    [Fact]
    public void PluginIngestionPlan_CallerMutatesSource_PreservesPlannedOrder()
    {
        var planned = new List<PlannedPlugin>
        {
            new PlannedPluginIngestion("First.esp"),
            new PlannedPluginSkip("Absent.esp", PlannedSkipReason.NotPresentInLoadOrder)
        };

        var plan = new PluginIngestionPlan(CreatePlanRequest("First.esp", "Absent.esp"), planned);
        planned.Reverse();

        Assert.Collection(
            plan.Plugins,
            entry => Assert.Equal("First.esp", entry.PluginName),
            entry => Assert.Equal("Absent.esp", entry.PluginName));
    }

    /// <summary>
    ///     Rejects plans that lose, add, rename, recase, or reorder the authoritative selection.
    /// </summary>
    [Theory]
    [InlineData()]
    [InlineData("First.esp")]
    [InlineData("First.esp", "Second.ESP", "Extra.esp")]
    [InlineData("Renamed.esp", "Second.ESP")]
    [InlineData("first.esp", "Second.ESP")]
    [InlineData("Second.ESP", "First.esp")]
    public void PluginIngestionPlan_EntriesDifferFromSelection_RejectsPlan(params string[] names)
    {
        var planned = names.Select(name => new PlannedPluginIngestion(name));

        var exception = Assert.Throws<ArgumentException>(() =>
            new PluginIngestionPlan(CreatePlanRequest("First.esp", "Second.ESP"), planned));

        Assert.Equal("plugins", exception.ParamName);
    }

    /// <summary>
    ///     Requires both the authoritative request and a non-null planned entry for each selection.
    /// </summary>
    [Fact]
    public void PluginIngestionPlan_NullInputs_RejectsMissingRequestCollectionOrEntry()
    {
        var request = CreatePlanRequest("First.esp");

        Assert.Equal("request", Assert.Throws<ArgumentNullException>(() =>
            new PluginIngestionPlan(null!, [new PlannedPluginIngestion("First.esp")])).ParamName);
        Assert.Equal("plugins", Assert.Throws<ArgumentNullException>(() =>
            new PluginIngestionPlan(request, null!)).ParamName);
        Assert.Equal("plugins", Assert.Throws<ArgumentException>(() =>
            new PluginIngestionPlan(request, [null!])).ParamName);
    }

    /// <summary>
    ///     Keeps only ordered planned entries after validation, so Store paths and other run facts cannot leak into a plan.
    /// </summary>
    [Fact]
    public void PluginIngestionPlan_TypeDefinition_RetainsAndExposesOnlyPlannedEntries()
    {
        var type = typeof(PluginIngestionPlan);
        var fields = type.GetFields(System.Reflection.BindingFlags.Instance |
                                    System.Reflection.BindingFlags.Public |
                                    System.Reflection.BindingFlags.NonPublic);

        Assert.Equal(typeof(System.Collections.Immutable.ImmutableArray<PlannedPlugin>), Assert.Single(fields).FieldType);
        Assert.Equal(nameof(PluginIngestionPlan.Plugins), Assert.Single(type.GetProperties()).Name);
    }

    /// <summary>
    ///     Verifies public Processing Run construction does not expose Plugin Ingestion's Game Load Orders or overlay
    ///     dependencies without pinning unrelated constructor evolution.
    /// </summary>
    [Fact]
    public void ProcessingRunExecutor_PublicConstruction_ExposesNoPluginIngestionAdapters()
    {
        var publicDependencyTypes = typeof(ProcessingRunExecutor)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.DoesNotContain(typeof(IGameLoadOrders), publicDependencyTypes);
        Assert.DoesNotContain(typeof(IPluginOverlayReader), publicDependencyTypes);
    }

    [Fact]
    public void PluginIngestionProgress_ValidFacts_CreatesStructuredPreparationAndCurrentPluginProgress()
    {
        var preparing = PluginIngestionProgress.PreparingLoadOrder(3);
        var current = PluginIngestionProgress.IngestingPlugin("Second.esp", 2, 3);

        Assert.Equal(PluginIngestionProgressStage.PreparingLoadOrder, preparing.Stage);
        Assert.Null(preparing.PluginName);
        Assert.Null(preparing.PluginPosition);
        Assert.Equal(3, preparing.TotalPluginCount);
        Assert.Equal(PluginIngestionProgressStage.IngestingPlugin, current.Stage);
        Assert.Equal("Second.esp", current.PluginName);
        Assert.Equal(2, current.PluginPosition);
        Assert.Equal(3, current.TotalPluginCount);
    }

    [Fact]
    public void SelectionRequest_CallerMutatesSource_PreservesCapturedOrder()
    {
        var pluginNames = new List<string> { "First.esp", "Second.esp" };
        var request = new SelectedPluginIngestionRequest(
            @"C:\Games\Skyrim",
            GameRelease.SkyrimSE,
            pluginNames,
            UpdateMode.Append);

        pluginNames[0] = "Changed.esp";
        pluginNames.Reverse();

        Assert.Equal(["First.esp", "Second.esp"], request.PluginNames);
    }

    [Fact]
    public void SelectionRequest_EmptySelection_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new SelectedPluginIngestionRequest(
            @"C:\Games\Skyrim",
            GameRelease.SkyrimSE,
            [],
            UpdateMode.Append));
    }

    [Fact]
    public void SelectionRequest_BlankPluginName_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new SelectedPluginIngestionRequest(
            @"C:\Games\Skyrim",
            GameRelease.SkyrimSE,
            ["First.esp", " "],
            UpdateMode.Append));
    }

    [Fact]
    public void SelectionRequest_CaseInsensitiveDuplicateNames_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new SelectedPluginIngestionRequest(
            @"C:\Games\Skyrim",
            GameRelease.SkyrimSE,
            ["Duplicate.esp", "DUPLICATE.ESP"],
            UpdateMode.Append));
    }

    [Fact]
    public void PluginOutcomes_ValidFacts_PreserveTypedClassificationAndBoundedDiagnostics()
    {
        var diagnosticDetails = new List<string>
        {
            "detail 1",
            "detail 2",
            "detail 3",
            "detail 4",
            "detail 5",
            "detail 6",
            "detail 7"
        };
        var warning = new ProcessingWarning(7, diagnosticDetails);
        var ingested = new IngestedPlugin("Ingested.esp", 42, warning);
        var skipped = new SkippedPlugin(
            "Skipped.esp",
            SkippedPluginReason.PluginFileUnavailable,
            Path.Combine(Path.GetTempPath(), "Skyrim", "Data", "Skipped.esp"));
        var failed = new FailedPlugin(
            "Failed.esp",
            new PluginReadDiagnostic(PluginReadPhase.ReadingRecords, "Invalid record data."));

        diagnosticDetails[0] = "changed";

        Assert.Equal(42, ingested.FormIdCount);
        Assert.Same(warning, ingested.Warning);
        Assert.Equal(7, warning.TotalIssueCount);
        Assert.Equal(["detail 1", "detail 2", "detail 3", "detail 4", "detail 5"], warning.DiagnosticDetails);
        Assert.Equal(2, warning.OmittedDetailCount);
        Assert.Equal(SkippedPluginReason.PluginFileUnavailable, skipped.Reason);
        Assert.Equal(FailedPluginReason.PluginReadFailed, failed.Reason);
        Assert.Equal([FailedPluginReason.PluginReadFailed], Enum.GetValues<FailedPluginReason>());
        Assert.Equal(PluginReadPhase.ReadingRecords, failed.Diagnostic.Phase);
        Assert.Equal("Invalid record data.", failed.Diagnostic.Message);
        Assert.Equal(
            [
                SkippedPluginReason.NotPresentInLoadOrder,
                SkippedPluginReason.PluginFileUnavailable,
                SkippedPluginReason.ZeroFormIdRecords
            ],
            Enum.GetValues<SkippedPluginReason>());
    }

    /// <summary>
    ///     Verifies Plugin Ingestion can retain the resolved unavailable-file fact without making Processing Run resolve
    ///     the GameRelease-specific Data path itself.
    /// </summary>
    [Fact]
    public void SkippedPlugin_UnavailableFile_PreservesResolvedPluginPath()
    {
        var pluginPath = Path.Combine(Path.GetTempPath(), "Skyrim", "Data", "Missing.esp");

        var skipped = new SkippedPlugin(
            "Missing.esp",
            SkippedPluginReason.PluginFileUnavailable,
            pluginPath);

        Assert.Equal(pluginPath, skipped.ResolvedPluginPath);
    }

    /// <summary>
    ///     Verifies an unavailable-file outcome cannot omit the resolved path owned by Plugin Ingestion.
    /// </summary>
    [Fact]
    public void SkippedPlugin_UnavailableFileWithoutResolvedPath_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new SkippedPlugin(
            "Missing.esp",
            SkippedPluginReason.PluginFileUnavailable));

        Assert.Equal("resolvedPluginPath", exception.ParamName);
    }

    /// <summary>
    ///     Verifies other typed skip reasons cannot carry an unrelated unavailable-file path.
    /// </summary>
    [Fact]
    public void SkippedPlugin_OtherReasonWithResolvedPath_ThrowsArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(() => new SkippedPlugin(
            "Missing.esp",
            SkippedPluginReason.NotPresentInLoadOrder,
            Path.Combine(Path.GetTempPath(), "Skyrim", "Data", "Missing.esp")));

        Assert.Equal("resolvedPluginPath", exception.ParamName);
        Assert.StartsWith(
            "A resolved Plugin path is only valid for an unavailable Plugin file.",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void IngestedPlugin_NonPositiveFormIdCount_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new IngestedPlugin("Empty.esp", 0));
    }

    [Fact]
    public void ProcessingWarning_DiagnosticCountExceedsTotal_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new ProcessingWarning(1, ["first", "second"]));
    }

    [Fact]
    public void Report_MatchingRequest_PreservesTypedOutcomeOrderAfterSourceMutation()
    {
        var request = CreateSelectionRequest("Ingested.esp", "Skipped.esp", "Failed.esp");
        var ingested = new IngestedPlugin("Ingested.esp", 3);
        var skipped = new SkippedPlugin("Skipped.esp", SkippedPluginReason.ZeroFormIdRecords);
        var failed = new FailedPlugin(
            "Failed.esp",
            new PluginReadDiagnostic(PluginReadPhase.OpeningPlugin, "Unreadable header."));
        var outcomes = new List<PluginIngestionOutcome> { ingested, skipped, failed };

        var report = new PluginIngestionReport(request, outcomes);
        outcomes.Reverse();

        Assert.Collection(
            report.Outcomes,
            outcome => Assert.Same(ingested, outcome),
            outcome => Assert.Same(skipped, outcome),
            outcome => Assert.Same(failed, outcome));
    }

    [Fact]
    public void Report_OutcomeCountDiffersFromRequest_ThrowsArgumentException()
    {
        var request = CreateSelectionRequest("First.esp", "Second.esp");

        Assert.Throws<ArgumentException>(() => new PluginIngestionReport(
            request,
            [new IngestedPlugin("First.esp", 1)]));
    }

    [Fact]
    public void Report_OutcomeOrderDiffersFromRequest_ThrowsArgumentException()
    {
        var request = CreateSelectionRequest("First.esp", "Second.esp");

        Assert.Throws<ArgumentException>(() => new PluginIngestionReport(
            request,
            [
                new IngestedPlugin("Second.esp", 1),
                new IngestedPlugin("First.esp", 1)
            ]));
    }

    private static SelectedPluginIngestionRequest CreateSelectionRequest(params string[] pluginNames)
    {
        return new SelectedPluginIngestionRequest(
            @"C:\Games\Skyrim",
            GameRelease.SkyrimSE,
            pluginNames,
            UpdateMode.Append);
    }
    /// <summary>
    ///     Creates an authoritative dry-run selection without requiring a Store path.
    /// </summary>
    private static PluginProcessingRunRequest CreatePlanRequest(params string[] names)
    {
        return new PluginProcessingRunRequest(
            @"C:\Games\Skyrim", string.Empty, GameRelease.SkyrimSE, names, UpdateMode.Append, dryRun: true);
    }

}
