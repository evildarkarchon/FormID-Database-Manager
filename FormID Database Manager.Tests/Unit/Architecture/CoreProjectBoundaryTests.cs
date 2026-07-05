using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
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
