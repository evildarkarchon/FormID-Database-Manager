#nullable enable

using System;
using System.IO;
using FormID_Database_Manager.Services;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

public class GameInstallationsTests
{
    private static readonly string GameRoot = Path.Combine(Path.GetTempPath(), "game-installations-tests", "SkyrimSE");

    [Fact]
    public void CanonicalizeDataDirectory_GameRootInput_ReturnsThatRootsDataDirectory()
    {
        var canonical = GameInstallations.CanonicalizeDataDirectory(GameRoot);

        Assert.Equal(Path.Combine(GameRoot, "Data"), canonical);
    }

    [Fact]
    public void CanonicalizeDataDirectory_DataDirectoryInput_ReturnsThatDataDirectory()
    {
        var dataDirectory = Path.Combine(GameRoot, "Data");

        var canonical = GameInstallations.CanonicalizeDataDirectory(dataDirectory);

        Assert.Equal(dataDirectory, canonical);
    }

    [Theory]
    [InlineData("Data")]
    [InlineData("data")]
    [InlineData("DATA")]
    public void CanonicalizeDataDirectory_DataDirectoryInputInAnyCasing_IsRecognizedAsADataDirectory(
        string dataFolderName)
    {
        var canonical = GameInstallations.CanonicalizeDataDirectory(Path.Combine(GameRoot, dataFolderName));

        // Casing of the supplied segment is preserved; what matters is that "Data" is not appended a second time.
        Assert.Equal(Path.Combine(GameRoot, dataFolderName), canonical);
    }

    [Theory]
    [InlineData("\\")]
    [InlineData("/")]
    public void CanonicalizeDataDirectory_DataDirectoryWithTrailingSeparator_MatchesThePlainForm(string separator)
    {
        var dataDirectory = Path.Combine(GameRoot, "Data");

        var canonical = GameInstallations.CanonicalizeDataDirectory(dataDirectory + separator);

        Assert.Equal(GameInstallations.CanonicalizeDataDirectory(dataDirectory), canonical);
        Assert.Equal(dataDirectory, canonical);
    }

    [Theory]
    [InlineData("\\")]
    [InlineData("/")]
    public void CanonicalizeDataDirectory_GameRootWithTrailingSeparator_MatchesThePlainForm(string separator)
    {
        var canonical = GameInstallations.CanonicalizeDataDirectory(GameRoot + separator);

        Assert.Equal(GameInstallations.CanonicalizeDataDirectory(GameRoot), canonical);
    }

    [Fact]
    public void CanonicalizeDataDirectory_MixedSeparators_MatchesThePlainForm()
    {
        var mixed = GameRoot.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + "/Data";

        var canonical = GameInstallations.CanonicalizeDataDirectory(mixed);

        Assert.Equal(GameInstallations.CanonicalizeDataDirectory(Path.Combine(GameRoot, "Data")), canonical);
    }

    [Fact]
    public void CanonicalizeDataDirectory_DotSegments_MatchesThePlainForm()
    {
        var expected = GameInstallations.CanonicalizeDataDirectory(Path.Combine(GameRoot, "Data"));

        Assert.Equal(expected, GameInstallations.CanonicalizeDataDirectory(Path.Combine(GameRoot, "Data", ".")));
        Assert.Equal(expected, GameInstallations.CanonicalizeDataDirectory(Path.Combine(GameRoot, "unused", "..")));
        Assert.Equal(
            expected,
            GameInstallations.CanonicalizeDataDirectory(Path.Combine(GameRoot, "Data", "unused", "..")));
    }

    [Fact]
    public void CanonicalizeDataDirectory_RelativeInput_ReturnsAFullyQualifiedPath()
    {
        var canonical = GameInstallations.CanonicalizeDataDirectory(Path.Combine("SkyrimSE", "Data"));

        Assert.True(Path.IsPathFullyQualified(canonical));
        Assert.EndsWith(Path.Combine("SkyrimSE", "Data"), canonical, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CanonicalizeDataDirectory_AnyInput_NeverEndsWithASeparator()
    {
        foreach (var input in new[]
                 {
                     GameRoot,
                     GameRoot + Path.DirectorySeparatorChar,
                     Path.Combine(GameRoot, "Data"),
                     Path.Combine(GameRoot, "Data") + Path.AltDirectorySeparatorChar
                 })
        {
            var canonical = GameInstallations.CanonicalizeDataDirectory(input);

            Assert.Equal(Path.TrimEndingDirectorySeparator(canonical), canonical);
        }
    }

    [Fact]
    public void CanonicalizeDataDirectory_DirectoriesThatDoNotExist_AnswersWithoutConsultingTheFileSystem()
    {
        // The rule is pure path arithmetic: an absent directory canonicalizes exactly like a present one.
        var absentRoot = Path.Combine(Path.GetTempPath(), $"absent-game-{Guid.NewGuid():N}");

        var canonical = GameInstallations.CanonicalizeDataDirectory(absentRoot);

        Assert.False(Directory.Exists(absentRoot));
        Assert.Equal(Path.Combine(absentRoot, "Data"), canonical);
    }

    [Fact]
    public void CanonicalizeDataDirectory_MissingInput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => GameInstallations.CanonicalizeDataDirectory(null!));
        Assert.Throws<ArgumentException>(() => GameInstallations.CanonicalizeDataDirectory("   "));
    }
}
