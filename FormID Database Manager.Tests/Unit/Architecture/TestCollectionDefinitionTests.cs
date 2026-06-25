using System;
using System.IO;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Architecture;

public class TestCollectionDefinitionTests
{
    public static TheoryData<string> SerializedCollectionNames =>
        ["Database Tests", "Integration Tests", "Performance Tests", "UI Tests"];

    [Theory]
    [MemberData(nameof(SerializedCollectionNames))]
    public void TestCollectionDefinitions_DisableParallelExecution(string collectionName)
    {
        var testCollectionsPath =
            Path.Combine(FindRepositoryRoot(), "FormID Database Manager.Tests", "TestCollections.cs");

        Assert.True(File.Exists(testCollectionsPath),
            $"Test collection definitions were not found at {testCollectionsPath}.");

        var source = File.ReadAllText(testCollectionsPath);

        Assert.Contains(
            $"[CollectionDefinition(\"{collectionName}\", DisableParallelization = true)]",
            source,
            StringComparison.Ordinal);
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
