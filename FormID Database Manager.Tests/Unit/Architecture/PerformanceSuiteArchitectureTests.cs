using System;
using System.IO;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Architecture;

public class PerformanceSuiteArchitectureTests
{
    /// <summary>
    ///     Verifies that performance coverage exercises the production Processing Run and FormID Record Store seams.
    /// </summary>
    [Fact]
    public void PerformanceSuites_DoNotReferenceRetiredProcessingAdapters()
    {
        var performanceDirectory = Path.Combine(
            FindRepositoryRoot(),
            "FormID Database Manager.Tests",
            "Performance");
        var retiredTypeNames = new[]
        {
            "PluginProcessingService",
            "FormIdTextProcessor",
            "ProcessingParameters"
        };

        foreach (var sourcePath in Directory.EnumerateFiles(
                     performanceDirectory,
                     "*.cs",
                     SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(sourcePath);
            foreach (var retiredTypeName in retiredTypeNames)
            {
                Assert.False(
                    source.Contains(retiredTypeName, StringComparison.Ordinal),
                    $"{Path.GetFileName(sourcePath)} references retired processing type {retiredTypeName}.");
            }
        }
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
