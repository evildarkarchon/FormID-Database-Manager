using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

public sealed class PluginIngestionContractTests
{
    /// <summary>
    ///     Verifies production Plugin Ingestion cannot be subclassed as an alternate Processing Run test seam.
    /// </summary>
    [Fact]
    public void PluginIngestion_TypeDefinition_IsInternalSealedInterfaceImplementation()
    {
        var implementationType = typeof(PluginIngestion);

        Assert.True(implementationType.IsNotPublic);
        Assert.True(implementationType.IsSealed);
        Assert.Contains(typeof(IPluginIngestion), implementationType.GetInterfaces());
    }

    [Fact]
    public void IPluginIngestion_TypeDefinition_IsInternalInterfaceWithOneSelectedSetOperation()
    {
        var interfaceType = typeof(IPluginIngestion);
        var operation = Assert.Single(interfaceType.GetMethods());
        var parameters = operation.GetParameters();

        Assert.True(interfaceType.IsInterface);
        Assert.True(interfaceType.IsNotPublic);
        Assert.Equal("IngestAsync", operation.Name);
        Assert.Equal(typeof(Task<PluginIngestionReport>), operation.ReturnType);
        Assert.Collection(
            parameters,
            parameter => Assert.Equal(typeof(SelectedPluginIngestionRequest), parameter.ParameterType),
            parameter => Assert.Equal(typeof(IFormIdRecordStoreSession), parameter.ParameterType),
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
        var skipped = new SkippedPlugin("Skipped.esp", SkippedPluginReason.PluginFileUnavailable);
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
}
