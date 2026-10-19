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
