#nullable enable

using System.Collections.Generic;
using FormID_Database_Manager.Services;
using Mutagen.Bethesda;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

/// <summary>
///     Covers the Processing Run request and failure contracts: what a request captures, what it refuses, and the exact
///     words a refusal reaches the user as.
/// </summary>
/// <remarks>
///     <para>
///         Issue #68, under parent #61. These sentences are user-facing — the User Workflow shows a validation failure
///         and an unresolvable master unwrapped, without the generic processing-error prefix (ADR-0006) — but they are
///         not rendered by <see cref="ProcessingRunPresentation" />: they are built by the types in
///         <c>ProcessingRunContracts.cs</c> and travel as exception messages. They therefore belong beside those types
///         rather than in the executor suite, which asserts what a run does and no longer asserts any wording at all.
///     </para>
///     <para>
///         An unresolvable master is worded two ways because the underlying failure sometimes names the master and
///         sometimes does not, and each variant tells the user to look at a different thing. Both are pinned.
///     </para>
/// </remarks>
public sealed class ProcessingRunContractTests
{
    private const string GameDirectory = @"C:\Games\Skyrim";
    private const string DatabasePath = @"C:\Databases\formids.db";

    /// <summary>
    ///     Verifies the validation message a Plugin request raises when it is given no game directory.
    /// </summary>
    [Fact]
    public void PluginProcessingRunRequest_BlankGameDirectory_UsesTheGameDirectoryValidationWording()
    {
        var exception = Assert.Throws<ProcessingRunValidationException>(() =>
            new PluginProcessingRunRequest(
                string.Empty,
                DatabasePath,
                GameRelease.SkyrimSE,
                ["First.esp"],
                UpdateMode.Append));

        Assert.Equal("Game directory must be specified when processing plugins", exception.Message);
    }

    /// <summary>
    ///     Verifies the validation message a request raises when it is given no Store path.
    /// </summary>
    [Fact]
    public void PluginProcessingRunRequest_BlankDatabasePath_UsesTheDatabasePathValidationWording()
    {
        var exception = Assert.Throws<ProcessingRunValidationException>(() =>
            new PluginProcessingRunRequest(
                GameDirectory,
                string.Empty,
                GameRelease.SkyrimSE,
                ["First.esp"],
                UpdateMode.Append));

        Assert.Equal("Database path must be specified", exception.Message);
    }

    /// <summary>
    ///     Verifies the validation message a Plugin request raises when nothing is selected.
    /// </summary>
    [Fact]
    public void PluginProcessingRunRequest_EmptyPluginNames_UsesTheEmptySelectionValidationWording()
    {
        var exception = Assert.Throws<ProcessingRunValidationException>(() =>
            new PluginProcessingRunRequest(
                GameDirectory,
                DatabasePath,
                GameRelease.SkyrimSE,
                [],
                UpdateMode.Append));

        Assert.Equal("No plugins selected", exception.Message);
    }

    /// <summary>
    ///     Verifies the validation message a Plugin request raises for a blank name inside the selection.
    /// </summary>
    [Fact]
    public void PluginProcessingRunRequest_BlankPluginName_UsesTheBlankNameValidationWording()
    {
        var exception = Assert.Throws<ProcessingRunValidationException>(() =>
            new PluginProcessingRunRequest(
                GameDirectory,
                DatabasePath,
                GameRelease.SkyrimSE,
                ["Valid.esp", " "],
                UpdateMode.Append));

        Assert.Equal("Plugin name must be specified", exception.Message);
    }

    /// <summary>
    ///     Verifies the validation message a Plugin request raises for a selection naming one Plugin twice, which the
    ///     selection snapshot compares without regard to case.
    /// </summary>
    [Fact]
    public void PluginProcessingRunRequest_CaseInsensitiveDuplicateNames_UsesTheUniqueNameValidationWording()
    {
        var exception = Assert.Throws<ProcessingRunValidationException>(() =>
            new PluginProcessingRunRequest(
                GameDirectory,
                DatabasePath,
                GameRelease.SkyrimSE,
                ["Duplicate.esp", "DUPLICATE.ESP"],
                UpdateMode.Append));

        Assert.Equal("Plugin names must be unique", exception.Message);
    }

    /// <summary>
    ///     Verifies the validation message a FormID text request raises when it names no text file.
    /// </summary>
    [Fact]
    public void FormIdTextProcessingRunRequest_BlankFormIdListPath_UsesTheTextFileValidationWording()
    {
        var exception = Assert.Throws<ProcessingRunValidationException>(() =>
            new FormIdTextProcessingRunRequest(
                string.Empty,
                DatabasePath,
                GameRelease.SkyrimSE,
                UpdateMode.Append));

        Assert.Equal("FormID text file must be specified", exception.Message);
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
