using System;
using System.IO;
using System.Reflection;
using FormID_Database_Manager.Services;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Exceptions;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

/// <summary>
///     Tests the one nested-cancellation unwrap that the overlay reader, Plugin Ingestion, and Entry Extraction share.
///     The overlay reader's opening path has no seam to drive a cancellation through, so this is that path's coverage;
///     enumeration and per-record cancellation are also covered end to end by <c>PluginIngestionTests</c> and
///     <c>EntryExtractionTests</c>.
/// </summary>
public sealed class NestedCancellationTests
{
    [Fact]
    public void RethrowIfPresent_CancellationInsideAMutagenRecordException_ThrowsTheOriginalCancellation()
    {
        var cancellation = new OperationCanceledException("Record read cancelled.");
        var wrapper = new RecordException(
            formKey: null,
            recordType: null,
            modKey: ModKey.FromNameAndExtension("Cancelled.esp"),
            edid: null,
            innerException: cancellation);

        var thrown = Assert.Throws<OperationCanceledException>(() => NestedCancellation.RethrowIfPresent(wrapper));

        Assert.Same(cancellation, thrown);
    }

    [Fact]
    public void RethrowIfPresent_CancellationSeveralWrappersDeep_ThrowsTheOriginalCancellation()
    {
        var cancellation = new OperationCanceledException("Getter cancelled.");
        var wrapper = new InvalidOperationException(
            "outer",
            new TargetInvocationException(cancellation));

        var thrown = Assert.Throws<OperationCanceledException>(() => NestedCancellation.RethrowIfPresent(wrapper));

        Assert.Same(cancellation, thrown);
    }

    [Fact]
    public void RethrowIfPresent_ExceptionIsItselfCancellation_ThrowsThatCancellation()
    {
        var cancellation = new OperationCanceledException("Plugin read cancelled.");

        var thrown = Assert.Throws<OperationCanceledException>(() => NestedCancellation.RethrowIfPresent(cancellation));

        Assert.Same(cancellation, thrown);
    }

    [Fact]
    public void RethrowIfPresent_NoCancellationInTheChain_ReturnsWithoutThrowing()
    {
        var failure = new InvalidOperationException("outer", new IOException("inner"));

        var thrown = Record.Exception(() => NestedCancellation.RethrowIfPresent(failure));

        Assert.Null(thrown);
    }
}
