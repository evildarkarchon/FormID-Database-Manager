using FormID_Database_Manager.Services;
using FormID_Database_Manager.TestUtilities.Builders;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Order;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Services;

public sealed class GameLoadOrderEnvironmentTests
{
    /// <summary>
    ///     Verifies production translation retains GameRelease, order, and ModKey filename semantics.
    /// </summary>
    [Fact]
    public void TranslateGameLoadOrder_MutagenListings_RetainsReleaseOrderAndPluginFilenames()
    {
        var listings = new ILoadOrderListingGetter[]
        {
            LoadOrderListing.CreateEnabled(ModKey.FromNameAndExtension("First.esm")),
            LoadOrderListing.CreateEnabled(ModKey.FromNameAndExtension("Second.esp"))
        };

        var translated = GameLoadOrderEnvironment.TranslateGameLoadOrder(GameRelease.SkyrimSE, listings);

        Assert.Equal(GameRelease.SkyrimSE, translated.GameRelease);
        Assert.Equal(["First.esm", "Second.esp"], translated.Listings.Select(listing => listing.PluginName));
    }

    /// <summary>
    ///     Verifies filesystem observation resolves both available and unavailable listing paths.
    /// </summary>
    [Fact]
    public void ObservePluginFile_ExistingAndMissingListings_ResolvesPathsAndKeepsAvailabilitySeparate()
    {
        var dataDirectory = CreateTemporaryDirectory();
        try
        {
            File.WriteAllText(Path.Combine(dataDirectory, "Available.esp"), string.Empty);
            var environment = new GameLoadOrderEnvironment();

            var available = environment.ObservePluginFile(
                dataDirectory,
                new GameLoadOrderListing("Available.esp"));
            var unavailable = environment.ObservePluginFile(
                dataDirectory,
                new GameLoadOrderListing("Unavailable.esp"));

            Assert.Equal(Path.Combine(dataDirectory, "Available.esp"), available.ResolvedPluginPath);
            Assert.True(available.IsAvailable);
            Assert.Equal(Path.Combine(dataDirectory, "Unavailable.esp"), unavailable.ResolvedPluginPath);
            Assert.False(unavailable.IsAvailable);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    /// <summary>
    ///     Verifies non-separated releases omit the lookup and never read an available Plugin header for style.
    /// </summary>
    [Fact]
    public void PreparePluginReads_NonSeparatedRelease_SuppliesNoMasterFlagsLookupOrStyleRead()
    {
        var environment = new GameLoadOrderEnvironment();
        var deliberatelyUnreadableAvailableFile = new PluginFileObservation(
            "Unreadable.esp",
            Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.esp"),
            IsAvailable: true);

        var capability = environment.PreparePluginReads(
            GameRelease.SkyrimSE,
            [deliberatelyUnreadableAvailableFile]);

        var payload = capability.RequirePayload<MutagenPluginReadCapabilityPayload>();
        Assert.Equal(GameRelease.SkyrimSE, payload.GameRelease);
        Assert.Null(payload.ReadParameters.MasterFlagsLookup);
    }

    /// <summary>
    ///     Verifies separated releases preserve a present empty lookup when no listed file is available.
    /// </summary>
    [Fact]
    public void PreparePluginReads_SeparatedReleaseWithNoAvailableListings_SuppliesEmptyMasterFlagsLookup()
    {
        var environment = new GameLoadOrderEnvironment();
        var unavailableFile = new PluginFileObservation(
            "Unavailable.esm",
            Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.esm"),
            IsAvailable: false);

        var capability = environment.PreparePluginReads(GameRelease.Starfield, [unavailableFile]);

        var payload = capability.RequirePayload<MutagenPluginReadCapabilityPayload>();
        Assert.Equal(GameRelease.Starfield, payload.GameRelease);
        Assert.NotNull(payload.ReadParameters.MasterFlagsLookup);
        Assert.Empty(payload.ReadParameters.MasterFlagsLookup.Items);
    }

    /// <summary>
    ///     Verifies separated preparation reads every available style and skips unavailable listings first.
    /// </summary>
    [Fact]
    public void PreparePluginReads_SeparatedRelease_ReadsStylesOnlyFromAvailableListings()
    {
        var dataDirectory = CreateTemporaryDirectory();
        try
        {
            var firstPluginPath = PluginFixture.Write(GameRelease.Starfield, dataDirectory, "First.esm");
            var secondPluginPath = PluginFixture.Write(GameRelease.Starfield, dataDirectory, "Second.esm");
            var listedPluginFiles = new[]
            {
                new PluginFileObservation(
                    "Unavailable.esm",
                    Path.Combine(dataDirectory, "Unavailable.esm"),
                    IsAvailable: false),
                new PluginFileObservation("First.esm", firstPluginPath, IsAvailable: true),
                new PluginFileObservation("Second.esm", secondPluginPath, IsAvailable: true)
            };
            var environment = new GameLoadOrderEnvironment();

            var capability = environment.PreparePluginReads(GameRelease.Starfield, listedPluginFiles);

            var payload = capability.RequirePayload<MutagenPluginReadCapabilityPayload>();
            Assert.Equal(
                ["First.esm", "Second.esm"],
                payload.ReadParameters.MasterFlagsLookup!.Items.Select(style => style.ModKey.FileName.ToString()));
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    /// <summary>
    ///     Creates one unique caller-owned directory for filesystem adapter tests.
    /// </summary>
    /// <returns>The absolute path of the created directory.</returns>
    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"FormIdManager-GameLoadOrders-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
