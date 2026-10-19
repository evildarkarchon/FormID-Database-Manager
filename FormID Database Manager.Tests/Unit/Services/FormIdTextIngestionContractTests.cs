using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FormID_Database_Manager.Services;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

public sealed class FormIdTextIngestionContractTests
{
    /// <summary>
    ///     Verifies FormID Text Ingestion's consumer-owned Store role exposes one streaming write that returns no value,
    ///     carries no Store lifetime or maintenance, and is inherited by the run-scoped Store session beside the Plugin
    ///     write role (ADR-0001, 2026-10-18).
    /// </summary>
    [Fact]
    public void IFormIdTextRowWriter_TypeDefinition_IsInternalSingleOperationRoleInheritedByStoreSession()
    {
        var writerType = typeof(IFormIdTextRowWriter);
        var storeSessionType = typeof(IFormIdRecordStoreSession);
        var writeOperation = Assert.Single(writerType.GetMethods());

        Assert.True(writerType.IsInterface);
        Assert.True(writerType.IsNotPublic);
        Assert.Equal("WriteTextRowsAsync", writeOperation.Name);
        Assert.Equal(typeof(Task), writeOperation.ReturnType);
        Assert.Collection(
            writeOperation.GetParameters(),
            parameter => Assert.Equal(typeof(IAsyncEnumerable<FormIdTextRow>), parameter.ParameterType),
            parameter => Assert.Equal(typeof(UpdateMode), parameter.ParameterType),
            parameter =>
            {
                Assert.Equal(typeof(CancellationToken), parameter.ParameterType);
                Assert.True(parameter.HasDefaultValue);
            });
        Assert.False(typeof(IAsyncDisposable).IsAssignableFrom(writerType));
        Assert.Contains(writerType, storeSessionType.GetInterfaces());
        Assert.Contains(typeof(IPluginFormIdRecordWriter), storeSessionType.GetInterfaces());
        Assert.DoesNotContain(
            storeSessionType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly),
            method => method.Name == "WriteTextRowsAsync");
    }

    /// <summary>
    ///     Verifies FormID Text Ingestion is an internal interface shaped like Plugin Ingestion's: an ingestion that
    ///     borrows only the text-row writer role and returns the import counts, and a plan that takes no Store at all.
    /// </summary>
    [Fact]
    public void IFormIdTextIngestion_TypeDefinition_IsInternalIngestAndPlanBorrowingOnlyTheTextRowRole()
    {
        var ingestionType = typeof(IFormIdTextIngestion);
        var ingest = Assert.Single(ingestionType.GetMethods(), method => method.Name == "IngestAsync");
        var plan = Assert.Single(ingestionType.GetMethods(), method => method.Name == "PlanAsync");

        Assert.True(ingestionType.IsInterface);
        Assert.True(ingestionType.IsNotPublic);
        Assert.Equal(2, ingestionType.GetMethods().Length);
        Assert.Equal(typeof(Task<FormIdTextFileImportResult>), ingest.ReturnType);
        Assert.Collection(
            ingest.GetParameters(),
            parameter => Assert.Equal(typeof(FormIdTextProcessingRunRequest), parameter.ParameterType),
            parameter => Assert.Equal(typeof(IFormIdTextRowWriter), parameter.ParameterType),
            parameter =>
            {
                Assert.Equal(typeof(IProgress<FormIdTextIngestionProgress>), parameter.ParameterType);
                Assert.True(parameter.HasDefaultValue);
            },
            parameter =>
            {
                Assert.Equal(typeof(CancellationToken), parameter.ParameterType);
                Assert.True(parameter.HasDefaultValue);
            });
        Assert.Equal(typeof(Task<FormIdTextRunPlan>), plan.ReturnType);
        Assert.Collection(
            plan.GetParameters(),
            parameter => Assert.Equal(typeof(FormIdTextProcessingRunRequest), parameter.ParameterType),
            parameter =>
            {
                Assert.Equal(typeof(CancellationToken), parameter.ParameterType);
                Assert.True(parameter.HasDefaultValue);
            });
    }

    /// <summary>
    ///     Verifies FormID Text Ingestion's progress is a closed internal hierarchy of exactly three cases — the
    ///     opening, a counter update, and a Plugin's first encounter — so no reader has to decode counters to tell
    ///     them apart (ADR-0007, 2026-10-18).
    /// </summary>
    [Fact]
    public void FormIdTextIngestionProgress_TypeDefinition_IsClosedInternalHierarchyOfThreeCases()
    {
        var progressType = typeof(FormIdTextIngestionProgress);
        var cases = progressType.Assembly
            .GetTypes()
            .Where(type => type != progressType && progressType.IsAssignableFrom(type))
            .ToArray();

        Assert.True(progressType.IsAbstract);
        Assert.True(progressType.IsNotPublic);
        // The record's compiler-generated copy constructor is protected, so the closing constructor is the
        // parameterless one, which must be private protected.
        Assert.True(Assert.Single(
            progressType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
            constructor => constructor.GetParameters().Length == 0).IsFamilyAndAssembly);
        Assert.Equal(
            [
                nameof(FormIdTextIngestionCounterUpdate),
                nameof(FormIdTextIngestionOpening),
                nameof(FormIdTextIngestionPluginFirstEncountered)
            ],
            cases.Select(static type => type.Name).Order(StringComparer.Ordinal));
        Assert.All(cases, type =>
        {
            Assert.True(type.IsSealed);
            Assert.True(type.IsNotPublic);
        });
    }

    /// <summary>
    ///     Verifies the parsed row handed to the Store is an internal value carrying exactly Plugin, FormID, and Entry.
    /// </summary>
    [Fact]
    public void FormIdTextRow_TypeDefinition_IsInternalValueWithPluginFormIdAndEntry()
    {
        var rowType = typeof(FormIdTextRow);

        Assert.True(rowType.IsValueType);
        Assert.True(rowType.IsNotPublic);
        Assert.Equal(
            ["Entry", "FormId", "Plugin"],
            rowType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Select(static property => property.Name)
                .Order(StringComparer.Ordinal));
    }
}
