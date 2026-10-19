using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FormID_Database_Manager.Models;
using FormID_Database_Manager.Services;
using FormID_Database_Manager.ViewModels;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Architecture;

public class CoreProjectBoundaryTests
{
    /// <summary>
    /// Verifies that the solution contains the UI-neutral core project required by the WinUI migration.
    /// </summary>
    [Fact]
    public void Solution_IncludesCoreProject()
    {
        var repositoryRoot = FindRepositoryRoot();
        var solutionPath = Path.Combine(repositoryRoot, "FormID Database Manager.slnx");

        var solutionContent = File.ReadAllText(solutionPath);

        Assert.Contains(
            "FormID Database Manager.Core/FormID Database Manager.Core.csproj",
            solutionContent,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that the core project remains free of legacy desktop UI package and source dependencies.
    /// </summary>
    [Fact]
    public void CoreProject_DoesNotReferenceLegacyDesktopUi()
    {
        var coreProjectDirectory = GetCoreProjectDirectory();
        var projectPath = Path.Combine(coreProjectDirectory, "FormID Database Manager.Core.csproj");
        var legacyDesktopPackagePrefix = string.Concat("Ava", "lonia");

        Assert.True(File.Exists(projectPath), $"Core project file was not found at {projectPath}.");

        var project = XDocument.Load(projectPath);
        var packageReferences = project
            .Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty);

        Assert.DoesNotContain(packageReferences, packageName =>
            packageName.Contains(legacyDesktopPackagePrefix, StringComparison.OrdinalIgnoreCase));

        var sourceFiles = Directory.EnumerateFiles(coreProjectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase));

        foreach (var sourceFile in sourceFiles)
        {
            var source = File.ReadAllText(sourceFile);
            Assert.DoesNotContain(string.Concat("using ", legacyDesktopPackagePrefix), source,
                StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Verifies that core exposes the file-dialog contract needed by desktop picker services.
    /// </summary>
    [Fact]
    public void CoreProject_DefinesFileDialogContract()
    {
        var contractPath = Path.Combine(GetCoreProjectDirectory(), "Services", "IFileDialogService.cs");

        Assert.True(File.Exists(contractPath), $"File-dialog contract was not found at {contractPath}.");

        var source = File.ReadAllText(contractPath);

        Assert.Contains("public interface IFileDialogService", source, StringComparison.Ordinal);
        Assert.Contains("Task<FileDialogResult> SelectGameDirectory()", source, StringComparison.Ordinal);
        Assert.Contains("Task<FileDialogResult> SelectDatabaseFile()", source, StringComparison.Ordinal);
        Assert.Contains("Task<FileDialogResult> SelectFormIdListFile()", source, StringComparison.Ordinal);

        var resultPath = Path.Combine(GetCoreProjectDirectory(), "Services", "FileDialogResult.cs");
        var resultSource = File.ReadAllText(resultPath);
        Assert.Contains("public enum FileDialogResultKind", resultSource, StringComparison.Ordinal);
        Assert.Contains("Success", resultSource, StringComparison.Ordinal);
        Assert.Contains("Cancelled", resultSource, StringComparison.Ordinal);
        Assert.Contains("Failure", resultSource, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that WinUI can only read the complete Game Context projection, its stable directory collection, and
    /// the Workflow Activity channel derived from the two projected activity facts.
    /// </summary>
    /// <param name="propertyName">The projected property whose public shape is verified.</param>
    /// <remarks>
    /// The progress properties are pinned here for the same reason the Game Context ones are: a public setter would
    /// let a third writer back onto the progress channel and reintroduce the collision the precedence rule removes.
    /// </remarks>
    [Theory]
    [InlineData(nameof(MainWindowViewModel.SelectedGame))]
    [InlineData(nameof(MainWindowViewModel.GameDirectory))]
    [InlineData(nameof(MainWindowViewModel.AdvancedMode))]
    [InlineData(nameof(MainWindowViewModel.DetectedDirectories))]
    [InlineData(nameof(MainWindowViewModel.ProgressStatus))]
    [InlineData(nameof(MainWindowViewModel.ProgressValue))]
    [InlineData(nameof(MainWindowViewModel.IsProgressVisible))]
    [InlineData(nameof(MainWindowViewModel.ProcessButtonText))]
    public void ProjectedProperties_PublicContract_ExposeGettersWithoutConsumerAccessibleSetters(string propertyName)
    {
        var property = Assert.Single(
            typeof(MainWindowViewModel).GetProperties(),
            candidate => candidate.Name == propertyName);

        Assert.NotNull(property.GetMethod);
        Assert.True(property.GetMethod.IsPublic);
        // Reflection includes private accessors, which let the ViewModel apply projections without exposing a writer.
        Assert.True(property.SetMethod is null or { IsPrivate: true });
    }

    /// <summary>
    /// Verifies that the progress surface a caller could write directly is gone, leaving the two activity projections
    /// as the only way onto the progress channel.
    /// </summary>
    /// <param name="memberName">The retired member name that must not reappear in any form.</param>
    [Theory]
    [InlineData("UpdateProgress")]
    [InlineData("ResetProgress")]
    [InlineData("IsScanning")]
    [InlineData("IsProcessing")]
    public void RetiredProgressMembers_ViewModelSurface_NoLongerExist(string memberName)
    {
        var members = typeof(MainWindowViewModel).GetMember(
            memberName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.Empty(members);
    }

    /// <summary>
    /// Verifies that production SQLite ownership remains inside the FormID Record Store and retired setup seams stay
    /// absent from every source area.
    /// </summary>
    [Fact]
    public void RepositorySources_AfterStoreOwnershipContract_KeepSqliteOwnershipInsideFormIdRecordStore()
    {
        var repositoryRoot = FindRepositoryRoot();
        var coreProjectDirectory = GetCoreProjectDirectory();
        var winUiProjectDirectory = Path.Combine(repositoryRoot, "FormID Database Manager.WinUI");
        var sourceDirectories = new[]
        {
            coreProjectDirectory,
            winUiProjectDirectory,
            Path.Combine(repositoryRoot, "FormID Database Manager.TestUtilities"),
            Path.Combine(repositoryRoot, "FormID Database Manager.Tests")
        };

        var sourceFiles = sourceDirectories
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            .Where(path => !IsBuildOutput(path))
            .ToArray();

        var retiredSetupTypeNames = new[]
        {
            string.Concat("Database", "Service"),
            string.Concat("Database", "Fixture")
        };
        var retiredSetupReferences = sourceFiles
            .Where(path => retiredSetupTypeNames.Any(typeName =>
                File.ReadAllText(path).Contains(typeName, StringComparison.Ordinal)))
            .Select(path => Path.GetRelativePath(repositoryRoot, path))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(retiredSetupReferences);

        var productionSqliteOwners = new[] { coreProjectDirectory, winUiProjectDirectory }
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            .Where(path => !IsBuildOutput(path))
            .Where(path => File.ReadAllText(path).Contains("Microsoft.Data.Sqlite", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(repositoryRoot, path))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { Path.Combine("FormID Database Manager.Core", "Services", "FormIdRecordStore.cs") },
            productionSqliteOwners);
    }

    /// <summary>
    ///     Verifies that the User Workflow's production composition is the only Core source that reads the local
    ///     application data directory, so the default database root is resolved once and injected everywhere else.
    /// </summary>
    [Fact]
    public void CoreSources_DefaultDatabaseRoot_IsResolvedOnlyByUserWorkflowComposition()
    {
        var coreProjectDirectory = GetCoreProjectDirectory();

        var localApplicationDataReaders = Directory
            .EnumerateFiles(coreProjectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Where(path => File.ReadAllText(path).Contains("LocalApplicationData", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(coreProjectDirectory, path))
            .ToArray();

        Assert.Equal(new[] { Path.Combine("Services", "UserWorkflow.cs") }, localApplicationDataReaders);
    }

    /// <summary>
    ///     Verifies that the wording the FormID Record Store used to write onto its progress path has not come back,
    ///     leaving both the words and the decision to name a Plugin to whoever renders the counters it reports.
    /// </summary>
    /// <remarks>
    ///     This pins the three retired sentences by name rather than proving the absence of prose in general, which no
    ///     source-text guard can do. What the app does say is pinned by <c>ProcessingRunPresentationTests</c>, not here.
    /// </remarks>
    [Fact]
    public void FormIdRecordStore_TextImportProgress_DoesNotContainTheRetiredProgressWording()
    {
        var storeSource = File.ReadAllText(
            Path.Combine(GetCoreProjectDirectory(), "Services", "FormIdRecordStore.cs"));

        Assert.DoesNotContain("Starting processing", storeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Processing plugin", storeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Completed processing", storeSource, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Verifies that every string a Processing Run once wrote has left the run, and that the event type those
    ///     strings travelled on is gone from every source area rather than reshaped.
    /// </summary>
    /// <remarks>
    ///     Issue #66, under parent #61. A run now reports typed progress and returns a typed outcome, so the executor
    ///     holds no user-facing wording at all. Like the Store guard above this pins the retired sentences by name
    ///     rather than proving the absence of prose in general, which no source-text guard can do; what the run does
    ///     say is pinned by <c>ProcessingRunPresentationTests</c> instead.
    /// </remarks>
    [Fact]
    public void ProcessingRunExecutor_AfterTypedProgress_ContainsNoUserFacingWordingAndNoRetiredEventType()
    {
        var repositoryRoot = FindRepositoryRoot();
        var processingRunSource = File.ReadAllText(
            Path.Combine(GetCoreProjectDirectory(), "Services", "ProcessingRun.cs"));

        foreach (var retiredWording in new[]
                 {
                     "Starting processing",
                     "Processing plugin",
                     "Processing: ",
                     "Initializing plugin ingestion",
                     // The space is what keeps this off the IngestingPlugin progress case, which is a type name the
                     // run legitimately mentions rather than wording it writes.
                     "Ingesting plugin",
                     "Completed processing",
                     "Processing completed",
                     "Error during processing",
                     "Would process"
                 })
        {
            Assert.DoesNotContain(retiredWording, processingRunSource, StringComparison.Ordinal);
        }

        // Split so this guard does not trip over its own spelling of the name it forbids.
        var retiredEventTypeName = string.Concat("ProcessingRun", "Event");
        var retiredEventReferences = new[]
            {
                GetCoreProjectDirectory(),
                Path.Combine(repositoryRoot, "FormID Database Manager.WinUI"),
                Path.Combine(repositoryRoot, "FormID Database Manager.TestUtilities"),
                Path.Combine(repositoryRoot, "FormID Database Manager.Tests")
            }
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            .Where(path => !IsBuildOutput(path))
            .Where(path => File.ReadAllText(path).Contains(retiredEventTypeName, StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(repositoryRoot, path))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(retiredEventReferences);
    }

    /// <summary>
    ///     Verifies that the Processing Run progress vocabulary has no failure case, and that Processing Run
    ///     Presentation no longer renders one: a failure reaches the user once, as an error message rendered from the
    ///     exception, never as transient progress the run's cleanup erases.
    /// </summary>
    /// <remarks>
    ///     Issue #104, under parent #102 (user stories 12 and 30). The cases are found by reflection, so this pins the
    ///     closed set the run may report rather than the absence of one name.
    /// </remarks>
    [Fact]
    public void ProcessingRunProgress_AfterTheStatelessExecutor_HasNoFailureCase()
    {
        var progressCases = typeof(ProcessingRunProgress).Assembly.GetTypes()
            .Where(static type => type.IsSubclassOf(typeof(ProcessingRunProgress)))
            .Select(static type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var presentationSource = File.ReadAllText(
            Path.Combine(GetCoreProjectDirectory(), "Services", "ProcessingRunPresentation.cs"));

        Assert.Equal(
            [
                nameof(ImportedFormIdText),
                nameof(ImportingFormIdText),
                nameof(IngestingPlugin),
                nameof(PreparingLoadOrder)
            ],
            progressCases);
        Assert.DoesNotContain("Error during processing", presentationSource, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Verifies that neither Processing Run Admission, Processing Run Control, the User Workflow nor the Game Context
    ///     writes a refusal sentence of its own: admission returns a typed reason and <c>ProcessingRunPresentation</c>
    ///     alone renders it (ADR-0010).
    /// </summary>
    /// <param name="sourceFileName">The Core service source that must hold no refusal wording.</param>
    /// <remarks>
    ///     Each sentence is pinned in full. The Game Context legitimately tells a Browse user it "Could not detect game
    ///     from directory. Please select a game from the dropdown.", so only the complete refusal sentence, ending in
    ///     "first.", is forbidden. What a refusal does say is pinned by <c>ProcessingRunPresentationTests</c>. Issue
    ///     #105 moved the refusal report from the User Workflow to Processing Run Control, which is the row that
    ///     matters now. The User Workflow row still guards something real: the workflow captures every press before
    ///     Processing Run Control sees it and writes its own picker errors straight to the ViewModel, so it is the one
    ///     place a pre-check, such as refusing an empty selection before the press is handed on, could show a refusal
    ///     that bypasses admission and Presentation.
    /// </remarks>
    [Theory]
    [InlineData("ProcessingRunAdmission.cs")]
    [InlineData("ProcessingRunControl.cs")]
    [InlineData("UserWorkflow.cs")]
    [InlineData("GameContext.cs")]
    public void RunAdmissionCallers_RefusalReasons_ContainNoRefusalSentence(string sourceFileName)
    {
        var source = File.ReadAllText(Path.Combine(GetCoreProjectDirectory(), "Services", sourceFileName));

        foreach (var refusalSentence in new[]
                 {
                     "Please select a game from the dropdown first.",
                     "Game directory must be specified when processing plugins",
                     "The plugin list is still loading for this directory.",
                     "The plugin list for this directory did not load.",
                     "No plugins selected",
                     "Choose a database file"
                 })
        {
            Assert.DoesNotContain(refusalSentence, source, StringComparison.Ordinal);
        }
    }

    /// <summary>
    ///     Verifies that neither the User Workflow nor Processing Run Control writes the starting or the
    ///     cancellation-requested status itself, or decides which run failure is shown unwrapped:
    ///     <c>ProcessingRunPresentation</c> renders all three (ADR-0006, ADR-0012).
    /// </summary>
    /// <param name="sourceFileName">The Core service source that reports run statuses and failures.</param>
    /// <remarks>
    ///     Issue #103, under parent #102; issue #105 added Processing Run Control, which now reports all three. The
    ///     statuses are searched for among the string literals rather than in the raw text, so a comment may still
    ///     quote them. The failure rule is pinned as the absence of a catch clause for
    ///     <see cref="UnresolvableMasterException" />, because such a clause is exactly how the rule used to live in the
    ///     User Workflow. What the user reads is pinned by <c>ProcessingRunPresentationTests</c>.
    /// </remarks>
    [Theory]
    [InlineData("UserWorkflow.cs")]
    [InlineData("ProcessingRunControl.cs")]
    public void RunReporters_RunStatusesAndFailures_AreRenderedOnlyByProcessingRunPresentation(string sourceFileName)
    {
        var source = File.ReadAllText(Path.Combine(GetCoreProjectDirectory(), "Services", sourceFileName));

        var literals = SourceStringLiterals.Extract(source).Select(static literal => literal.Text).ToArray();

        Assert.DoesNotContain("Initializing...", literals);
        Assert.DoesNotContain("Cancelling...", literals);
        Assert.DoesNotMatch(@"catch\s*\(\s*UnresolvableMasterException\b", source);
    }

    /// <summary>
    ///     Verifies that Processing Run Control holds no user-facing sentence at all: the only string literals it may
    ///     contain are exception diagnostics (ADR-0012).
    /// </summary>
    /// <remarks>
    ///     Issue #105, under parent #102. Like the admission guard below, this closes the set rather than pattern-matching
    ///     wording. The permitted set outside exception constructors is empty, so any new literal fails until a reviewer
    ///     decides whether it is wording, which belongs in <c>ProcessingRunPresentation</c>, or something that earns a
    ///     place in this list. A literal passed straight to an exception constructor is diagnostic text for a
    ///     programming error, not something a run says.
    /// </remarks>
    [Fact]
    public void ProcessingRunControl_StringLiterals_AreOnlyExceptionDiagnostics()
    {
        var controlSource = File.ReadAllText(
            Path.Combine(GetCoreProjectDirectory(), "Services", "ProcessingRunControl.cs"));
        string[] permittedLiterals = [];

        var literals = SourceStringLiterals.Extract(controlSource);
        var unexpectedLiterals = literals
            .Where(static literal => !literal.IsExceptionArgument)
            .Select(static literal => literal.Text)
            .Where(text => !permittedLiterals.Contains(text, StringComparer.Ordinal))
            .ToArray();

        // Guards the guard: the module's own unreachable-case diagnostic proves the scanner still reads this file.
        Assert.Contains(literals, static literal => literal.IsExceptionArgument);
        Assert.Empty(unexpectedLiterals);
    }

    /// <summary>
    ///     Verifies that Processing Run Control reads no Game Context, Plugin List, or ViewModel: it takes inputs the
    ///     User Workflow already captured and reports through its sink (ADR-0012).
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Issue #105, under parent #102. Two checks, because either alone has a gap. Reflection over every field of
    ///         the module and its nested types shows it holds none of them. The source text, comments included, shows it
    ///         names none of them, so it cannot reach one through a parameter or a static either.
    ///     </para>
    ///     <para>
    ///         The text search is on whole type names. The captured inputs legitimately carry a
    ///         <c>PluginListState</c> and a <c>PluginListSource</c>, which are values, and run activity is an
    ///         <see cref="ActivityProjection" /> from the ViewModels namespace, so neither "PluginList" as a prefix nor
    ///         the namespace can be forbidden.
    ///     </para>
    /// </remarks>
    [Fact]
    public void ProcessingRunControl_References_NoGameContextPluginListOrViewModel()
    {
        Type[] forbiddenTypes =
        [
            typeof(GameContext),
            typeof(PluginList),
            typeof(PluginListPresentationAdapter),
            typeof(MainWindowViewModel),
            typeof(UserWorkflow)
        ];
        const BindingFlags allFields = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                       BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var fieldTypes = new[] { typeof(ProcessingRunControl) }
            .Concat(typeof(ProcessingRunControl).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            .SelectMany(type => type.GetFields(allFields))
            .Select(static field => field.FieldType)
            .ToArray();
        var controlSource = File.ReadAllText(
            Path.Combine(GetCoreProjectDirectory(), "Services", "ProcessingRunControl.cs"));

        // Guards the guard: if reflection stopped finding fields, the field assertion would pass vacuously.
        Assert.Contains(typeof(IProcessingRunExecutor), fieldTypes);
        Assert.Empty(fieldTypes.Intersect(forbiddenTypes));
        var namedTypes = forbiddenTypes
            .Select(static type => type.Name)
            .Where(name => Regex.IsMatch(controlSource, $@"\b{Regex.Escape(name)}\b"))
            .ToArray();
        Assert.Empty(namedTypes);
    }

    /// <summary>
    ///     Verifies that Processing Run Admission holds no user-facing sentence at all, not only none of the known
    ///     refusals: every string literal it contains is either a default-path segment or exception diagnostic text
    ///     (issue #89, ADR-0010).
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         No wording heuristic can do this. "No plugins selected" carries no punctuation and is shorter than the
    ///         "FormID Database Manager" folder name, so punctuation, word counts and capitalisation would all either
    ///         miss a real refusal or reject a real path segment. The guard therefore closes the set instead: admission
    ///         may contain only the literals listed here, so a new literal fails until a reviewer decides whether it is
    ///         wording (which belongs in <c>ProcessingRunPresentation</c>) or another path segment (which joins the
    ///         list).
    ///     </para>
    ///     <para>
    ///         Comments are skipped, so a doc comment may still quote a sentence, and an interpolated literal is reduced
    ///         to its literal text, so <c>$"{tableName}.db"</c> counts as <c>.db</c>. A literal passed straight to an
    ///         exception constructor is exempt: it is diagnostic text for a programming or input error, which reaches
    ///         the user only behind the generic unexpected-failure prefix that <c>ProcessingRunPresentation</c> owns.
    ///     </para>
    /// </remarks>
    [Fact]
    public void ProcessingRunAdmission_StringLiterals_AreOnlyDefaultPathSegmentsOrExceptionDiagnostics()
    {
        var admissionSource = File.ReadAllText(
            Path.Combine(GetCoreProjectDirectory(), "Services", "ProcessingRunAdmission.cs"));
        string[] defaultPathSegments = ["FormID Database Manager", "Databases", ".db"];

        var unexpectedLiterals = SourceStringLiterals.Extract(admissionSource)
            .Where(static literal => !literal.IsExceptionArgument)
            .Select(static literal => literal.Text)
            .Where(text => !defaultPathSegments.Contains(text, StringComparer.Ordinal))
            .ToArray();

        Assert.Empty(unexpectedLiterals);
    }

    /// <summary>
    ///     Verifies the literal scanner the admission guard depends on, so that guard cannot pass because the scanner
    ///     stopped seeing literals.
    /// </summary>
    [Fact]
    public void SourceStringLiterals_MixedSource_FindsEveryLiteralOutsideComments()
    {
        const string source =
            "// \"in a line comment\"\n" +
            "/* \"in a block comment\" */\n" +
            "private const string Folder = \"Databases\";\n" +
            "var quote = '\"';\n" +
            "var path = $\"{Table(\"inner\")}.db\";\n" +
            "var verbatim = @\"C:\\Games \"\"quoted\"\"\";\n" +
            "var refusal = \"No plugins selected\";\n" +
            "throw new ArgumentException(\"Bad directory.\", nameof(path));\n";

        var literals = SourceStringLiterals.Extract(source);

        Assert.Equal(
            [
                ("Databases", false),
                ("inner", false),
                (".db", false),
                (@"C:\Games ""quoted""", false),
                ("No plugins selected", false),
                ("Bad directory.", true)
            ],
            literals.Select(static literal => (literal.Text, literal.IsExceptionArgument)));
    }

    /// <summary>
    ///     Verifies that the dry-run wording a substantive plan replaced has not come back anywhere in Core.
    /// </summary>
    /// <remarks>
    ///     Issue #67, under parent #61. A dry run used to echo the names the user had just selected — "Would process X"
    ///     — which told them nothing they had not typed. It now reports would-ingest and would-skip per Plugin, and a
    ///     text dry run reports the file's presence and size. Like the guards above this pins the retired sentence by
    ///     name; the wording that replaced it is pinned by <c>ProcessingRunPresentationTests</c> instead.
    /// </remarks>
    [Fact]
    public void CoreSources_AfterTheSubstantiveDryRun_DoNotContainTheRetiredPlanWording()
    {
        var retiredPlanWording = string.Concat("Would ", "process");
        var retiredPlanWordingOwners = Directory
            .EnumerateFiles(GetCoreProjectDirectory(), "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Where(path => File.ReadAllText(path).Contains(retiredPlanWording, StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(FindRepositoryRoot(), path))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(retiredPlanWordingOwners);
    }

    /// <summary>
    ///     Verifies that the User Workflow reaches Game Installation resolution only through the Game Context it
    ///     composes: it keeps no reference to <c>GameInstallations</c> and calls none of its members (ADR-0011).
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The pin is semantic rather than a bare text search for the type name. The User Workflow constructs the
    ///         Game Context, so its internal constructor still receives <c>GameInstallations</c> and <c>Create</c>
    ///         still builds the production one, and both hand it straight to <c>new GameContext(...)</c>. What would
    ///         put resolution back in the workflow is holding the module or calling into it, so that is what is
    ///         forbidden: no instance field of that type, checked by reflection, and no call to detection, location or
    ///         canonicalization, checked in the source text.
    ///     </para>
    ///     <para>
    ///         Taking a pre-built Game Context instead would have changed where the Game Context is constructed, and
    ///         taking only the installation probe would have hidden the dependency without removing it.
    ///     </para>
    /// </remarks>
    [Fact]
    public void UserWorkflow_GameInstallationResolution_IsReachedOnlyThroughTheGameContext()
    {
        const BindingFlags instanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var fieldTypes = typeof(UserWorkflow).GetFields(instanceFields).Select(static field => field.FieldType).ToArray();
        var userWorkflowSource = File.ReadAllText(Path.Combine(GetCoreProjectDirectory(), "Services", "UserWorkflow.cs"));

        Assert.DoesNotContain(typeof(GameInstallations), fieldTypes);
        Assert.Contains(typeof(GameContext), fieldTypes);
        foreach (var resolutionCall in new[] { ".Detect(", ".GetInstalledDirectories(", "CanonicalizeDataDirectory(" })
        {
            Assert.DoesNotContain(resolutionCall, userWorkflowSource, StringComparison.Ordinal);
        }
    }

    /// <summary>
    ///     Verifies that <see cref="UserWorkflow.Dispose" /> detaches the Plugin List presentation adapter, then retires
    ///     the Game Context, then retires Processing Run Control, which cancels the active Processing Run, then disposes
    ///     the Plugin List, in that order (#98, #105), and that no teardown step reaches the executor (#104).
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This is a source pin rather than a behavioural test because part of the order cannot be observed.
    ///         Retiring the Game Context publishes nothing at the moment it runs and only stops later work, so whether
    ///         the adapter detaches before or after it changes nothing a test could see. The two observable halves —
    ///         adapter before cancellation, and Game Context before cancellation — are also covered behaviourally by
    ///         the <c>Dispose_</c> tests in <c>UserWorkflowTests</c>; this pin holds the whole sequence in one place.
    ///     </para>
    ///     <para>
    ///         The calls are searched for only inside the method's body, and each must appear there exactly once, so a
    ///         duplicated or relocated call cannot satisfy the order by accident.
    ///     </para>
    ///     <para>
    ///         Issue #104, under parent #102. The executor is stateless and no longer disposable, so the body may not
    ///         mention the executor at all. Issue #105 moved the run's cancellation into Processing Run Control, which
    ///         owns the executor, so the third step is now retiring that module.
    ///     </para>
    /// </remarks>
    [Fact]
    public void UserWorkflowDispose_TeardownOrder_RetiresPresentationGameContextProcessingRunControlThenPluginList()
    {
        var userWorkflowSource = File.ReadAllText(Path.Combine(GetCoreProjectDirectory(), "Services", "UserWorkflow.cs"));
        const string disposeSignature = "public void Dispose()";
        var disposeStart = userWorkflowSource.IndexOf(disposeSignature, StringComparison.Ordinal);
        Assert.True(disposeStart >= 0, "UserWorkflow.Dispose was not found in the source.");
        Assert.Equal(disposeStart, userWorkflowSource.LastIndexOf(disposeSignature, StringComparison.Ordinal));
        // A member body closes on a brace indented one level, the class body's member indentation.
        var disposeEnd = Regex.Match(userWorkflowSource[disposeStart..], @"\n    \}\r?\n");
        Assert.True(disposeEnd.Success, "The end of UserWorkflow.Dispose was not found in the source.");
        var disposeBody = userWorkflowSource.Substring(disposeStart, disposeEnd.Index);
        string[] orderedSteps =
        [
            "RetireCollaborator(_pluginListPresentationAdapter.Dispose,",
            "RetireCollaborator(_gameContext.Dispose,",
            "RetireCollaborator(_processingRunControl.Dispose,",
            "RetireCollaborator(_pluginList.Dispose,"
        ];

        Assert.DoesNotContain("_processingRunExecutor", disposeBody, StringComparison.Ordinal);

        var stepPositions = orderedSteps
            .Select(step =>
            {
                var position = disposeBody.IndexOf(step, StringComparison.Ordinal);
                Assert.True(position >= 0, $"UserWorkflow.Dispose no longer calls {step}");
                Assert.Equal(position, disposeBody.LastIndexOf(step, StringComparison.Ordinal));
                return position;
            })
            .ToArray();

        Assert.Equal(stepPositions.Order(), stepPositions);
    }

    /// <summary>
    ///     Verifies that the Game Context references no Processing Run type: it decides which Plugin List can be loaded,
    ///     and the User Workflow alone captures that for Processing Run Control, which admits and runs (ADR-0011,
    ///     ADR-0012).
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The run types are not listed by hand. Every Core type whose name contains the word "Run" — a "Run"
    ///         followed by an upper-case letter or nothing, so <c>ProcessingRunAdmission</c>, <c>AdmittedRun</c> and
    ///         <c>ProcessingRunControl</c> count — is found by reflection, so a new run type is covered the day it is
    ///         added.
    ///     </para>
    ///     <para>
    ///         The check is on raw source text, comments included, because a <c>cref</c> in a doc comment is a
    ///         reference too. The Game Context's own <c>Task.Run</c> is a method, not a type, and is not matched.
    ///     </para>
    /// </remarks>
    [Fact]
    public void GameContextSource_ProcessingRunTypes_AreNotReferenced()
    {
        var runTypeNames = typeof(GameContext).Assembly.GetTypes()
            .Select(static type => type.Name.Split('`')[0])
            .Where(static name => !name.Contains('<', StringComparison.Ordinal))
            .Where(static name => Regex.IsMatch(name, "Run(?![a-z])"))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var gameContextSource = File.ReadAllText(Path.Combine(GetCoreProjectDirectory(), "Services", "GameContext.cs"));

        // Guards the guard: if reflection stopped finding run types, the assertion below would pass vacuously.
        Assert.Contains(nameof(ProcessingRunAdmission), runTypeNames);
        Assert.Contains(nameof(ProcessingRunControl), runTypeNames);
        var referencedRunTypes = runTypeNames
            .Where(name => Regex.IsMatch(gameContextSource, $@"\b{Regex.Escape(name)}\b"))
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(referencedRunTypes);
    }

    /// <summary>
    ///     Verifies that the Game Context is the only Core writer of the ViewModel's Game Context projection.
    /// </summary>
    /// <remarks>
    ///     The ViewModel's own file defines the member and is the other legitimate mention. Tests call it directly to
    ///     exercise the ViewModel, which is why only Core sources are scanned.
    /// </remarks>
    [Fact]
    public void CoreSources_GameContextProjection_IsWrittenOnlyByTheGameContext()
    {
        var projectionWriters = Directory
            .EnumerateFiles(GetCoreProjectDirectory(), "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Where(path => File.ReadAllText(path).Contains("ApplyGameContextProjection(", StringComparison.Ordinal))
            .Select(path => Path.GetFileName(path))
            .OrderBy(static fileName => fileName, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["GameContext.cs", "MainWindowViewModel.cs"], projectionWriters);
    }

    /// <summary>
    ///     Verifies that the game-root-or-Data rule reaches a Processing Run only through the Plugin List Source.
    /// </summary>
    /// <remarks>
    ///     ADR-0002, as amended by ADR-0010 and ADR-0011. <c>GameInstallations</c> owns the rule and calls it from
    ///     detection, and <c>PluginListSource.Create</c> is the one place a run's Data directory is decided. The Game
    ///     Context makes that decision when it adopts a directory, as pinned by
    ///     <see cref="ProductionSources_PluginListSourceCreation_HappensOnlyInTheGameContext" />. Admission, the
    ///     executor, the run and Plugin Ingestion read <c>PluginListSource.DataDirectory</c> from the captured source
    ///     and the request instead of deciding it again, so a second caller anywhere in Core is a second place the run's
    ///     Data directory could diverge.
    /// </remarks>
    [Fact]
    public void CoreSources_GameRootOrDataRule_ReachesARunOnlyThroughThePluginListSource()
    {
        var canonicalizationCallers = Directory
            .EnumerateFiles(GetCoreProjectDirectory(), "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Where(path => File.ReadAllText(path).Contains("CanonicalizeDataDirectory(", StringComparison.Ordinal))
            .Select(path => Path.GetFileName(path))
            .OrderBy(static fileName => fileName, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["GameInstallations.cs", "PluginListSource.cs"], canonicalizationCallers);
    }

    /// <summary>
    ///     Verifies that the Game Context is the only production code that builds a Plugin List Source, so there is one
    ///     notion of "same directory" and an unusable directory is refused at the one moment it is picked (ADR-0011).
    /// </summary>
    /// <remarks>
    ///     Processing Run Admission used to build a second source from a captured raw directory, which gave it an
    ///     exception mode for a malformed one. It now compares the Game Context's own source, so any other production
    ///     caller would bring that second, later validation back. Tests build sources directly and are not scanned.
    /// </remarks>
    [Fact]
    public void ProductionSources_PluginListSourceCreation_HappensOnlyInTheGameContext()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceCreators = new[]
            {
                GetCoreProjectDirectory(),
                Path.Combine(repositoryRoot, "FormID Database Manager.WinUI")
            }
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            .Where(path => !IsBuildOutput(path))
            .Where(path => File.ReadAllText(path).Contains("PluginListSource.Create(", StringComparison.Ordinal))
            .Select(path => Path.GetFileName(path))
            .OrderBy(static fileName => fileName, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["GameContext.cs"], sourceCreators);
    }

    /// <summary>
    ///     Verifies the final selected-Plugin dependency direction: Processing Run owns the complete Store session,
    ///     while Plugin Ingestion borrows only Plugin-write behavior and owns its read-side adapters.
    /// </summary>
    [Fact]
    public void CoreServices_SelectedPluginOwnership_KeepsAdaptersAndStoreLifecycleOnTheirOwningSides()
    {
        var servicesDirectory = Path.Combine(GetCoreProjectDirectory(), "Services");
        var processingRunSource = File.ReadAllText(Path.Combine(servicesDirectory, "ProcessingRun.cs"));
        var pluginIngestionSource = File.ReadAllText(Path.Combine(servicesDirectory, "PluginIngestion.cs"));
        var ingestionOperation = Assert.Single(
            typeof(IPluginIngestion).GetMethods(),
            method => method.Name == nameof(IPluginIngestion.IngestAsync));
        var planningOperation = Assert.Single(
            typeof(IPluginIngestion).GetMethods(),
            method => method.Name == nameof(IPluginIngestion.PlanAsync));
        var pluginWriterType = typeof(IPluginFormIdRecordWriter);

        Assert.Contains("IPluginIngestion", processingRunSource, StringComparison.Ordinal);
        Assert.Contains("IFormIdRecordStoreSession recordStore", processingRunSource, StringComparison.Ordinal);
        Assert.Contains("IFormIdRecordStoreSessionOpener", processingRunSource, StringComparison.Ordinal);
        Assert.Contains("OpenAsync(", processingRunSource, StringComparison.Ordinal);
        Assert.Contains("OptimizeAsync(", processingRunSource, StringComparison.Ordinal);
        Assert.Contains("DisposeAsync(", processingRunSource, StringComparison.Ordinal);
        Assert.DoesNotContain("IGameLoadOrders", processingRunSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GameLoadOrders", processingRunSource, StringComparison.Ordinal);
        Assert.DoesNotContain("IPluginOverlayReader", processingRunSource, StringComparison.Ordinal);
        Assert.DoesNotContain("MutagenPluginOverlayReader", processingRunSource, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "GameInstallations.CanonicalizeDataDirectory",
            processingRunSource,
            StringComparison.Ordinal);

        Assert.Contains("IGameLoadOrders", pluginIngestionSource, StringComparison.Ordinal);
        Assert.Contains("PrepareSelectedPlugins", pluginIngestionSource, StringComparison.Ordinal);
        Assert.Contains("IPluginOverlayReader", pluginIngestionSource, StringComparison.Ordinal);
        Assert.Contains(
            ingestionOperation.GetParameters(),
            parameter => parameter.ParameterType == pluginWriterType);
        Assert.DoesNotContain(
            ingestionOperation.GetParameters(),
            parameter => parameter.ParameterType == typeof(IFormIdRecordStoreSession));
        Assert.DoesNotContain(
            planningOperation.GetParameters(),
            parameter => parameter.ParameterType == typeof(IFormIdRecordStoreSession));
        Assert.DoesNotContain("BinaryReadParameters", pluginIngestionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("File.Exists", pluginIngestionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("FormIdRecordStore.OpenAsync", pluginIngestionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("OptimizeAsync(", pluginIngestionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DisposeAsync(", pluginIngestionSource, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Verifies the Core assembly exposes only the aggregate implementation and no retired one-Plugin transport.
    /// </summary>
    [Fact]
    public void CoreAssembly_PluginIngestionContract_HasOneProductionImplementationAndNoRetiredProtocol()
    {
        var coreAssembly = typeof(PluginIngestion).Assembly;
        var contractType = typeof(IPluginIngestion);
        var implementations = coreAssembly
            .GetTypes()
            .Where(type => !type.IsAbstract && contractType.IsAssignableFrom(type))
            .ToArray();

        Assert.Equal([typeof(PluginIngestion)], implementations);

        var serviceNamespace = typeof(PluginIngestion).Namespace;
        var retiredTypeNames = new[]
        {
            string.Concat("PluginIngestion", "Request"),
            string.Concat("PluginIngestion", "Result"),
            string.Concat("PluginIngestion", "ResultKind")
        };

        Assert.All(retiredTypeNames, typeName =>
            Assert.Null(coreAssembly.GetType($"{serviceNamespace}.{typeName}")));
    }

    /// <summary>
    ///     Prevents a second selected-Plugin authority or standalone capture module from returning.
    /// </summary>
    [Theory]
    [InlineData("SelectedPluginIngestionRequest")]
    [InlineData("PluginSelectionSnapshot")]
    public void CoreAssembly_SelectedPluginAuthority_ContainsNoRetiredType(string retiredTypeName)
    {
        Assert.DoesNotContain(typeof(PluginProcessingRunRequest).Assembly.GetTypes(),
            type => type.Name == retiredTypeName);
    }

    /// <summary>
    ///     Keeps both ingestion operations on the public request and prevents the retired executor mapper returning.
    /// </summary>
    [Fact]
    public void ProcessingRunExecutor_SelectedPluginAuthority_HasNoRequestMapper()
    {
        Assert.All(typeof(IPluginIngestion).GetMethods(), method =>
            Assert.Equal(typeof(PluginProcessingRunRequest), method.GetParameters()[0].ParameterType));
        Assert.DoesNotContain(typeof(ProcessingRunExecutor).GetMethods(
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic),
            method => method.Name == "CreateIngestionRequest");
    }

    /// <summary>
    ///     Verifies production exposes one authoritative Plugin List path and only a read-only presentation projection.
    /// </summary>
    [Fact]
    public void CoreAssembly_PluginListOwnership_HasNoRetiredMutableCollectionProtocol()
    {
        var coreAssembly = typeof(PluginList).Assembly;
        var serviceNamespace = typeof(PluginList).Namespace;
        var retiredTypeNames = new[]
        {
            string.Concat("PluginList", "Manager"),
            string.Concat("PluginList", "Refresh"),
            string.Concat("IPluginList", "Refresh"),
            string.Concat("PluginListRefresh", "Request"),
            string.Concat("PluginListRefresh", "Status"),
            string.Concat("PluginListRefresh", "Progress"),
            string.Concat("PluginListRefresh", "Result"),
            string.Concat("PluginList", "Activity"),
            string.Concat("PluginListNoSource", "Activity"),
            string.Concat("PluginListRefreshing", "Activity"),
            string.Concat("PluginListReady", "Activity"),
            string.Concat("PluginListFailed", "Activity"),
            string.Concat("PluginListCancelled", "Activity"),
            string.Concat("PluginListFaulted", "Activity")
        };

        Assert.All(retiredTypeNames, typeName =>
            Assert.Null(coreAssembly.GetType($"{serviceNamespace}.{typeName}")));

        var pluginsProperty = typeof(MainWindowViewModel).GetProperty(nameof(MainWindowViewModel.Plugins));
        Assert.NotNull(pluginsProperty);
        Assert.Equal(typeof(ReadOnlyObservableCollection<PluginListItem>), pluginsProperty.PropertyType);
        Assert.Null(typeof(MainWindowViewModel).GetMethod(
            string.Concat("GetSelected", "Plugins"),
            BindingFlags.Instance | BindingFlags.Public));

        var publicMethodsWithMutablePluginCollections = typeof(MainWindowViewModel)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.GetParameters().Any(parameter =>
                parameter.ParameterType == typeof(ObservableCollection<PluginListItem>)))
            .Select(method => method.Name)
            .ToArray();

        Assert.Empty(publicMethodsWithMutablePluginCollections);
    }

    private static bool IsBuildOutput(string path)
    {
        return path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                   StringComparison.OrdinalIgnoreCase)
               || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string GetCoreProjectDirectory()
    {
        return Path.Combine(FindRepositoryRoot(), "FormID Database Manager.Core");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FormID Database Manager.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from the test output directory.");
    }

    /// <summary>
    ///     One string literal found in C# source text.
    /// </summary>
    /// <param name="Text">
    ///     The literal's text as written, with verbatim doubled quotes collapsed and interpolation holes removed.
    /// </param>
    /// <param name="IsExceptionArgument">
    ///     Whether the literal appears in the argument list of an exception constructor in its statement.
    /// </param>
    private readonly record struct SourceStringLiteral(string Text, bool IsExceptionArgument);

    /// <summary>
    ///     A deliberately small C# scanner that finds string literals outside comments, for source-text guards that
    ///     have to tell wording from code.
    /// </summary>
    /// <remarks>
    ///     It understands line and block comments, character literals, and regular, verbatim and interpolated strings,
    ///     including literals nested in an interpolation hole. It does not reduce raw string literals: their whole
    ///     content is reported as written, which can only make a guard stricter, never let wording slip past it.
    /// </remarks>
    private static class SourceStringLiterals
    {
        private static readonly Regex ExceptionConstruction = new(
            @"new\s+[\w.]*Exception\s*\(",
            RegexOptions.CultureInvariant);

        /// <summary>
        ///     Finds every string literal in <paramref name="source" />, in source order, with a nested literal
        ///     reported before the interpolated literal that contains it.
        /// </summary>
        /// <param name="source">The C# source text to scan.</param>
        /// <returns>The literals found outside comments.</returns>
        internal static IReadOnlyList<SourceStringLiteral> Extract(string source)
        {
            var literals = new List<SourceStringLiteral>();
            // The code of the current statement so far, which decides whether a literal is an exception argument.
            var statement = new StringBuilder();
            var index = 0;
            while (index < source.Length)
            {
                var current = source[index];
                var next = index + 1 < source.Length ? source[index + 1] : '\0';
                if (current == '/' && next == '/')
                {
                    var lineEnd = source.IndexOf('\n', index);
                    index = lineEnd < 0 ? source.Length : lineEnd + 1;
                }
                else if (current == '/' && next == '*')
                {
                    var commentEnd = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
                    index = commentEnd < 0 ? source.Length : commentEnd + 2;
                }
                else if (current == '\'')
                {
                    index = SkipCharLiteral(source, index);
                }
                else if (TryFindStringStart(source, index, out var quoteIndex, out var interpolated, out var verbatim))
                {
                    var isExceptionArgument = ExceptionConstruction.IsMatch(statement.ToString());
                    index = ReadString(source, quoteIndex, interpolated, verbatim, isExceptionArgument, literals);
                }
                else
                {
                    if (current is ';' or '{' or '}')
                    {
                        statement.Clear();
                    }
                    else
                    {
                        statement.Append(current);
                    }

                    index++;
                }
            }

            return literals;
        }

        /// <summary>
        ///     Recognises a string literal's opening, including any <c>$</c> and <c>@</c> prefix, at
        ///     <paramref name="index" />.
        /// </summary>
        private static bool TryFindStringStart(
            string source,
            int index,
            out int quoteIndex,
            out bool interpolated,
            out bool verbatim)
        {
            interpolated = false;
            verbatim = false;
            quoteIndex = index;
            while (quoteIndex < source.Length && source[quoteIndex] is '$' or '@')
            {
                interpolated |= source[quoteIndex] == '$';
                verbatim |= source[quoteIndex] == '@';
                quoteIndex++;
            }

            return quoteIndex < source.Length && source[quoteIndex] == '"';
        }

        /// <summary>
        ///     Reads one string literal whose opening quote is at <paramref name="quoteIndex" /> and records it, after
        ///     any literal nested in one of its interpolation holes.
        /// </summary>
        /// <returns>The index just past the literal's closing quote.</returns>
        private static int ReadString(
            string source,
            int quoteIndex,
            bool interpolated,
            bool verbatim,
            bool isExceptionArgument,
            List<SourceStringLiteral> literals)
        {
            if (source.AsSpan(quoteIndex).StartsWith("\"\"\""))
            {
                return ReadRawString(source, quoteIndex, isExceptionArgument, literals);
            }

            var text = new StringBuilder();
            var index = quoteIndex + 1;
            while (index < source.Length)
            {
                var current = source[index];
                var next = index + 1 < source.Length ? source[index + 1] : '\0';
                if (current == '"' && verbatim && next == '"')
                {
                    text.Append('"');
                    index += 2;
                }
                else if (current == '"')
                {
                    index++;
                    break;
                }
                else if (current == '\\' && !verbatim)
                {
                    // An escape is kept as written; only its second character must not end the literal.
                    text.Append(current).Append(next);
                    index += 2;
                }
                else if (interpolated && current is '{' or '}' && next == current)
                {
                    text.Append(current);
                    index += 2;
                }
                else if (interpolated && current == '{')
                {
                    index = SkipInterpolationHole(source, index + 1, isExceptionArgument, literals);
                }
                else
                {
                    text.Append(current);
                    index++;
                }
            }

            literals.Add(new SourceStringLiteral(text.ToString(), isExceptionArgument));
            return index;
        }

        /// <summary>
        ///     Reads a raw string literal opened by three or more quotes and records its content as written.
        /// </summary>
        /// <returns>The index just past the closing quote run.</returns>
        private static int ReadRawString(
            string source,
            int quoteIndex,
            bool isExceptionArgument,
            List<SourceStringLiteral> literals)
        {
            var quoteCount = 0;
            while (quoteIndex + quoteCount < source.Length && source[quoteIndex + quoteCount] == '"')
            {
                quoteCount++;
            }

            var delimiter = new string('"', quoteCount);
            var contentStart = quoteIndex + quoteCount;
            var contentEnd = source.IndexOf(delimiter, contentStart, StringComparison.Ordinal);
            if (contentEnd < 0)
            {
                contentEnd = source.Length;
            }

            literals.Add(new SourceStringLiteral(source[contentStart..contentEnd], isExceptionArgument));
            return Math.Min(source.Length, contentEnd + quoteCount);
        }

        /// <summary>
        ///     Skips one interpolation hole starting just after its opening brace, recording any literal inside it.
        /// </summary>
        /// <returns>The index just past the hole's closing brace.</returns>
        private static int SkipInterpolationHole(
            string source,
            int index,
            bool isExceptionArgument,
            List<SourceStringLiteral> literals)
        {
            var depth = 1;
            while (index < source.Length)
            {
                var current = source[index];
                if (current == '\'')
                {
                    index = SkipCharLiteral(source, index);
                }
                else if (TryFindStringStart(source, index, out var quoteIndex, out var interpolated, out var verbatim))
                {
                    index = ReadString(source, quoteIndex, interpolated, verbatim, isExceptionArgument, literals);
                }
                else
                {
                    depth += current switch
                    {
                        '{' => 1,
                        '}' => -1,
                        _ => 0
                    };
                    index++;
                    if (depth == 0)
                    {
                        break;
                    }
                }
            }

            return index;
        }

        /// <summary>
        ///     Skips one character literal, so a quote character written as <c>'"'</c> does not open a string.
        /// </summary>
        /// <returns>The index just past the closing apostrophe.</returns>
        private static int SkipCharLiteral(string source, int index)
        {
            index++;
            while (index < source.Length && source[index] != '\'')
            {
                index += source[index] == '\\' ? 2 : 1;
            }

            return index + 1;
        }
    }
}
