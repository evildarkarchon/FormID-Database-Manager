#nullable enable

using System.Collections.Generic;
using FormID_Database_Manager.Services;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

/// <summary>
///     Covers the Processing Run request and failure contracts: what a request captures, which invariants its
///     constructor guards, and the exact words an unresolvable master reaches the user as.
/// </summary>
/// <remarks>
///     <para>
///         Issue #68, under parent #61, and ADR-0010. Processing Run Admission decides whether a press becomes a run
///         before any request is built, and its refusals are rendered by <see cref="ProcessingRunPresentation" />. A
///         request constructor therefore only guards its invariants as programming errors: it throws an ordinary
///         <see cref="ArgumentException" /> that names the violated parameter, and its message is diagnostic text no
///         user reads. Those guards are pinned by parameter name, with the message prefix telling apart the Plugin
///         selection invariants that share one parameter.
///     </para>
///     <para>
///         The unresolvable master sentences are still user-facing — the User Workflow shows that failure unwrapped,
///         without the generic processing-error prefix (ADR-0006) — and they travel as the exception's message, so
///         they belong beside the type that builds them. An unresolvable master is worded two ways because the
///         underlying failure sometimes names the master and sometimes does not, and each variant tells the user to
///         look at a different thing. Both are pinned.
///     </para>
/// </remarks>
public sealed class ProcessingRunContractTests
{
    private const string GameDirectory = @"C:\Games\Skyrim";
    private const string DatabasePath = @"C:\Databases\formids.db";

    /// <summary>
    ///     Pins which invariant is reported first when paths and selections contain competing invalid inputs.
    /// </summary>
    [Theory]
    [InlineData("", "", false, null, "databasePath", "Database path must be specified")]
    [InlineData("", "", true, null, "gameDirectory", "Game directory must be specified")]
    [InlineData("", DatabasePath, false, new string[0], "gameDirectory", "Game directory must be specified")]
    [InlineData(GameDirectory, DatabasePath, false, new[] { "Same.esp", "SAME.ESP", " " }, "pluginNames",
        "Plugin name must be specified")]
    public void PluginProcessingRunRequest_MultipleInvalidInputs_PreservesInvariantPrecedence(
        string gameDirectory, string databasePath, bool dryRun, string[]? pluginNames, string expectedParamName,
        string expectedMessagePrefix)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new PluginProcessingRunRequest(gameDirectory, databasePath, GameRelease.SkyrimSE,
                pluginNames!, UpdateMode.Append, dryRun));

        Assert.Equal(expectedParamName, exception.ParamName);
        Assert.StartsWith(expectedMessagePrefix, exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Keeps a null selection distinct from a malformed selection after valid paths have been checked.
    /// </summary>
    [Fact]
    public void PluginProcessingRunRequest_NullSelection_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new PluginProcessingRunRequest(GameDirectory, DatabasePath, GameRelease.SkyrimSE,
                null!, UpdateMode.Append));

        Assert.Equal("pluginNames", exception.ParamName);
    }

    /// <summary>
    ///     Allows explicit Dry Run facts without a Store path or a Confirmed Plugin List.
    /// </summary>
    [Fact]
    public void PluginProcessingRunRequest_DryRunWithoutStorePath_PreservesExplicitFacts()
    {
        var request = new PluginProcessingRunRequest(GameDirectory, "", GameRelease.SkyrimSE,
            ["SECOND.esp", "first.ESP"], UpdateMode.ReplacePluginRecords, dryRun: true);

        Assert.Equal(GameDirectory, request.GameDirectory);
        Assert.Equal("", request.DatabasePath);
        Assert.Equal(GameRelease.SkyrimSE, request.GameRelease);
        Assert.Equal(UpdateMode.ReplacePluginRecords, request.UpdateMode);
        Assert.True(request.DryRun);
        Assert.Equal(["SECOND.esp", "first.ESP"], request.PluginNames);
    }

    /// <summary>
    ///     Prevents deferred enumeration or writable collection exposure from changing an accepted selection.
    /// </summary>
    [Fact]
    public void PluginProcessingRunRequest_LazySelection_CapturesOnceAndRejectsMutation()
    {
        var enumerationCount = 0;
        var request = new PluginProcessingRunRequest(GameDirectory, DatabasePath, GameRelease.SkyrimSE,
            EnumerateNames(), UpdateMode.Append);

        Assert.Equal(1, enumerationCount);
        var names = Assert.IsAssignableFrom<IList<string>>(request.PluginNames);
        Assert.Throws<NotSupportedException>(() => names[0] = "Changed.esp");
        Assert.Equal(["SECOND.esp", "first.ESP"], request.PluginNames);
        Assert.Equal(1, enumerationCount);

        // The count exposes any second traversal of the caller-owned sequence.
        IEnumerable<string> EnumerateNames()
        {
            enumerationCount++;
            yield return "SECOND.esp";
            yield return "first.ESP";
        }
    }

    /// <summary>
    ///     Verifies that a Plugin request given no game directory throws a programming-error guard naming it.
    /// </summary>
    [Fact]
    public void PluginProcessingRunRequest_BlankGameDirectory_ThrowsArgumentExceptionForTheGameDirectory()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new PluginProcessingRunRequest(
                string.Empty,
                DatabasePath,
                GameRelease.SkyrimSE,
                ["First.esp"],
                UpdateMode.Append));

        Assert.Equal("gameDirectory", exception.ParamName);
    }

    /// <summary>
    ///     Verifies that a real run's request given no Store path throws a programming-error guard naming it.
    /// </summary>
    [Fact]
    public void PluginProcessingRunRequest_BlankDatabasePath_ThrowsArgumentExceptionForTheDatabasePath()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new PluginProcessingRunRequest(
                GameDirectory,
                string.Empty,
                GameRelease.SkyrimSE,
                ["First.esp"],
                UpdateMode.Append));

        Assert.Equal("databasePath", exception.ParamName);
    }

    /// <summary>
    ///     Verifies that a Plugin request with nothing selected throws a programming-error guard for the selection.
    /// </summary>
    [Fact]
    public void PluginProcessingRunRequest_EmptyPluginNames_ThrowsArgumentExceptionForTheSelection()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new PluginProcessingRunRequest(
                GameDirectory,
                DatabasePath,
                GameRelease.SkyrimSE,
                [],
                UpdateMode.Append));

        Assert.Equal("pluginNames", exception.ParamName);
        Assert.StartsWith("Plugin selection must not be empty", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Verifies that a blank name inside the selection throws a programming-error guard for the selection.
    /// </summary>
    [Fact]
    public void PluginProcessingRunRequest_BlankPluginName_ThrowsArgumentExceptionForTheSelection()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new PluginProcessingRunRequest(
                GameDirectory,
                DatabasePath,
                GameRelease.SkyrimSE,
                ["Valid.esp", " "],
                UpdateMode.Append));

        Assert.Equal("pluginNames", exception.ParamName);
        Assert.StartsWith("Plugin name must be specified", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Verifies that a selection naming one Plugin twice, which the selection snapshot compares without regard to
    ///     case, throws a programming-error guard for the selection.
    /// </summary>
    [Fact]
    public void PluginProcessingRunRequest_CaseInsensitiveDuplicateNames_ThrowsArgumentExceptionForTheSelection()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new PluginProcessingRunRequest(
                GameDirectory,
                DatabasePath,
                GameRelease.SkyrimSE,
                ["Duplicate.esp", "DUPLICATE.ESP"],
                UpdateMode.Append));

        Assert.Equal("pluginNames", exception.ParamName);
        Assert.StartsWith("Plugin names must be unique", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Verifies that a FormID text request naming no text file throws a programming-error guard naming it.
    /// </summary>
    [Fact]
    public void FormIdTextProcessingRunRequest_BlankFormIdListPath_ThrowsArgumentExceptionForTheTextFilePath()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new FormIdTextProcessingRunRequest(
                string.Empty,
                DatabasePath,
                GameRelease.SkyrimSE,
                UpdateMode.Append));

        Assert.Equal("formIdListPath", exception.ParamName);
    }

    /// <summary>
    ///     Verifies that the deleted validation exception has not come back: a request refuses invalid input only as an
    ///     ordinary argument exception, because Processing Run Admission decides refusals before a request exists.
    /// </summary>
    [Fact]
    public void ProcessingRunContracts_AfterAdmission_DefineNoValidationExceptionType()
    {
        var retiredTypeName = string.Concat("FormID_Database_Manager.Services.ProcessingRun", "ValidationException");

        Assert.Null(typeof(ProcessingRunRequest).Assembly.GetType(retiredTypeName));
    }

    /// <summary>
    ///     Verifies that a request captures the selection it was given, so a caller mutating its own list afterwards
    ///     cannot change what the run processes or the order it processes them in.
    /// </summary>
    [Fact]
    public void PluginProcessingRunRequest_CallerMutatesSource_PreservesCapturedNamesAndOrder()
    {
        var pluginNames = new List<string> { "First.esp", "Second.esp" };
        var request = new PluginProcessingRunRequest(
            GameDirectory,
            DatabasePath,
            GameRelease.SkyrimSE,
            pluginNames,
            UpdateMode.Append);

        pluginNames[0] = "Changed.esp";
        pluginNames.Reverse();

        Assert.Equal(["First.esp", "Second.esp"], request.PluginNames);
    }

    /// <summary>
    ///     Verifies the wording of an unresolvable master the underlying failure named, which points the user at the
    ///     Data directory that must supply it.
    /// </summary>
    /// <remarks>
    ///     ADR-0006, issue #52. The wording deliberately stays on the Data directory rather than the Plugin, because
    ///     pointing the user at the Plugin would send them to fix something that is not broken. The User Workflow shows
    ///     this message unwrapped for the same reason: burying it behind the generic prefix would waste the one piece
    ///     of information the failure exists to deliver.
    /// </remarks>
    [Fact]
    public void UnresolvableMasterException_NamedMaster_NamesItAndTheDataDirectoryThatMustSupplyIt()
    {
        var exception = new UnresolvableMasterException("User.esp", "Starfield.esm");

        Assert.Equal(
            "Could not resolve 'Starfield.esm', a master file declared by User.esp. This game separates master " +
            "files by type in the load order, so that master must be present in the Data directory being " +
            "processed before any selected plugin can be read.",
            exception.Message);
    }

    /// <summary>
    ///     Verifies the wording for the case where the underlying failure named no individual master, which reports
    ///     that no master file was found at all rather than naming one it never learned.
    /// </summary>
    [Fact]
    public void UnresolvableMasterException_WithoutANamedMaster_ReportsThatNoMasterFileWasFound()
    {
        var exception = new UnresolvableMasterException("User.esp", null);

        Assert.Equal(
            "Could not resolve the master files declared by User.esp. This game separates master files by type " +
            "in the load order, but no master file was found in the Data directory being processed.",
            exception.Message);
    }
}
