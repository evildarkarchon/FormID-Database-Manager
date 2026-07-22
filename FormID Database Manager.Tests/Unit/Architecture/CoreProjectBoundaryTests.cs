using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
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
    ///     Verifies the final selected-Plugin dependency direction: Processing Run owns the Store lifecycle, while the
    ///     sealed aggregate Plugin Ingestion owns load-order, Data-path, and overlay adapters behind its interface.
    /// </summary>
    [Fact]
    public void CoreServices_SelectedPluginOwnership_KeepsAdaptersAndStoreLifecycleOnTheirOwningSides()
    {
        var servicesDirectory = Path.Combine(GetCoreProjectDirectory(), "Services");
        var processingRunSource = File.ReadAllText(Path.Combine(servicesDirectory, "ProcessingRun.cs"));
        var pluginIngestionSource = File.ReadAllText(Path.Combine(servicesDirectory, "PluginIngestion.cs"));

        Assert.Contains("IPluginIngestion", processingRunSource, StringComparison.Ordinal);
        Assert.Contains("_recordStoreOpener.OpenAsync", processingRunSource, StringComparison.Ordinal);
        Assert.Contains("recordStore.OptimizeAsync", processingRunSource, StringComparison.Ordinal);
        Assert.Contains("recordStore.DisposeAsync", processingRunSource, StringComparison.Ordinal);
        Assert.DoesNotContain("IGameLoadOrderProvider", processingRunSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GameLoadOrderProvider", processingRunSource, StringComparison.Ordinal);
        Assert.DoesNotContain("IPluginOverlayReader", processingRunSource, StringComparison.Ordinal);
        Assert.DoesNotContain("MutagenPluginOverlayReader", processingRunSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GameReleaseHelper.ResolveDataPath", processingRunSource, StringComparison.Ordinal);

        Assert.Contains("IGameLoadOrderProvider", pluginIngestionSource, StringComparison.Ordinal);
        Assert.Contains("IPluginOverlayReader", pluginIngestionSource, StringComparison.Ordinal);
        Assert.Contains("GameReleaseHelper.ResolveDataPath", pluginIngestionSource, StringComparison.Ordinal);
        Assert.Contains("IFormIdRecordStoreSession recordStore", pluginIngestionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("FormIdRecordStore.OpenAsync", pluginIngestionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("recordStore.OptimizeAsync", pluginIngestionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("recordStore.DisposeAsync", pluginIngestionSource, StringComparison.Ordinal);
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
            string.Concat("PluginListRefresh", "Result")
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
}
