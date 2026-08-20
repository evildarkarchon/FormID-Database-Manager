using System.Reflection;
using FormID_Database_Manager.Services;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Architecture;

public sealed class GameLoadOrdersArchitectureTests
{
    /// <summary>
    ///     Pins the single production implementation and the two role-oriented operations of the highest seam.
    /// </summary>
    [Fact]
    public void CoreAssembly_GameLoadOrdersContract_HasOneProductionImplementationAndExactlyTwoRoleOperations()
    {
        var contract = typeof(IGameLoadOrders);
        var implementations = contract.Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && contract.IsAssignableFrom(type))
            .ToArray();
        var operations = contract.GetMethods(BindingFlags.Instance | BindingFlags.Public);

        Assert.Equal([typeof(GameLoadOrders)], implementations);
        Assert.Equal(
            [nameof(IGameLoadOrders.DiscoverAvailablePluginsAsync), nameof(IGameLoadOrders.PrepareSelectedPlugins)],
            operations.Select(method => method.Name).Order(StringComparer.Ordinal));
    }

    /// <summary>
    ///     Prevents Boolean policy and Mutagen listing, style, or binary-read types from leaking through the seam.
    /// </summary>
    [Fact]
    public void GameLoadOrdersContract_RoleOperations_ExposeNoBooleanOrMutagenPreparationTypes()
    {
        var operations = typeof(IGameLoadOrders).GetMethods(BindingFlags.Instance | BindingFlags.Public);
        var exposedTypes = operations
            .SelectMany(operation => operation.GetParameters().Select(parameter => parameter.ParameterType)
                .Append(operation.ReturnType))
            .SelectMany(FlattenType)
            .ToArray();
        var forbiddenTypes = new[]
        {
            typeof(bool),
            typeof(ILoadOrderListingGetter),
            typeof(IModMasterStyledGetter),
            typeof(BinaryReadParameters)
        };

        Assert.DoesNotContain(exposedTypes, forbiddenTypes.Contains);
    }

    /// <summary>
    ///     Pins direct Plugin List and Plugin Ingestion use of the highest Game Load Orders seam and the complete
    ///     absence of the retired forwarding discovery layer.
    /// </summary>
    [Fact]
    public void LiveCallers_GameLoadOrdersCutover_UseHighestSeamWithoutForwardingDiscoveryLayer()
    {
        var servicesDirectory = Path.Combine(FindRepositoryRoot(), "FormID Database Manager.Core", "Services");
        var pluginListSource = File.ReadAllText(Path.Combine(servicesDirectory, "PluginList.cs"));
        var ingestionSource = File.ReadAllText(Path.Combine(servicesDirectory, "PluginIngestion.cs"));
        var retiredTypeNames = new[]
        {
            "IPluginListDiscovery",
            "PluginListDiscoveryProgress",
            "PluginListDiscoveryResult",
            "PluginListDiscoveryCompleted",
            "PluginListDiscoveryFailed"
        };

        Assert.Contains("IGameLoadOrders", pluginListSource, StringComparison.Ordinal);
        Assert.Contains("DiscoverAvailablePluginsAsync", pluginListSource, StringComparison.Ordinal);
        Assert.DoesNotContain("IGameLoadOrderProvider", pluginListSource, StringComparison.Ordinal);
        Assert.Contains("IGameLoadOrders", ingestionSource, StringComparison.Ordinal);
        Assert.Contains("PrepareSelectedPlugins", ingestionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("IGameLoadOrderProvider", ingestionSource, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(servicesDirectory, "PluginListDiscovery.cs")));
        Assert.All(
            retiredTypeNames,
            retiredTypeName => Assert.Null(typeof(IGameLoadOrders).Assembly.GetType(
                $"{typeof(IGameLoadOrders).Namespace}.{retiredTypeName}")));
    }

    /// <summary>
    ///     Pins the live overlay seam to one intact ready selected-Plugin case without exposing binary preparation.
    /// </summary>
    [Fact]
    public void PluginOverlayReaderContract_LiveOperation_AcceptsOnlyPreparedReadyCase()
    {
        var operation = Assert.Single(typeof(IPluginOverlayReader).GetMethods());
        var parameter = Assert.Single(operation.GetParameters());

        Assert.Equal(nameof(IPluginOverlayReader.ReadOverlay), operation.Name);
        Assert.Equal(typeof(SelectedPluginReady), parameter.ParameterType);
    }

    /// <summary>
    ///     Pins one cohesive lower environment seam and prevents the retired delegate-bundle substitution shape.
    /// </summary>
    [Fact]
    public void GameLoadOrdersConstruction_LowerEnvironmentSeam_UsesNoDelegateBundle()
    {
        var moduleConstructors = typeof(GameLoadOrders).GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var environmentConstructor = Assert.Single(
            moduleConstructors,
            constructor => constructor.GetParameters()
                .Select(parameter => parameter.ParameterType)
                .SequenceEqual([typeof(IGameLoadOrderEnvironment)]));
        var delegateParameters = moduleConstructors
            .Concat(typeof(GameLoadOrderEnvironment).GetConstructors(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            .SelectMany(constructor => constructor.GetParameters())
            .Where(parameter => typeof(Delegate).IsAssignableFrom(parameter.ParameterType))
            .ToArray();

        Assert.NotNull(environmentConstructor);
        Assert.Empty(delegateParameters);
    }

    /// <summary>
    ///     Keeps overlay opening, record enumeration, and FormID Record Store work outside the new module slice.
    /// </summary>
    [Fact]
    public void GameLoadOrdersSources_PreparationOwnership_ContainsNoOverlayEnumerationOrStoreWork()
    {
        var servicesDirectory = Path.Combine(FindRepositoryRoot(), "FormID Database Manager.Core", "Services");
        var source = string.Join(
            Environment.NewLine,
            File.ReadAllText(Path.Combine(servicesDirectory, "GameLoadOrders.cs")),
            File.ReadAllText(Path.Combine(servicesDirectory, "GameLoadOrderEnvironment.cs")));

        Assert.DoesNotContain("IPluginOverlayReader", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateOverlay", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EnumeratePluginRecords", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FormIdRecordStore", source, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Recursively expands generic, array, and by-reference wrappers so forbidden types cannot hide inside a role
    ///     operation's public type shape.
    /// </summary>
    /// <param name="type">The type shape to expand.</param>
    /// <returns>The type and every nested type exposed through it.</returns>
    private static IEnumerable<Type> FlattenType(Type type)
    {
        yield return type;

        if (type.HasElementType && type.GetElementType() is { } elementType)
        {
            foreach (var nestedType in FlattenType(elementType))
            {
                yield return nestedType;
            }
        }

        foreach (var genericArgument in type.GetGenericArguments())
        {
            foreach (var nestedType in FlattenType(genericArgument))
            {
                yield return nestedType;
            }
        }
    }

    /// <summary>
    ///     Locates the repository root from the architecture-test output directory.
    /// </summary>
    /// <returns>The repository root containing the solution file.</returns>
    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FormID Database Manager.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Could not locate repository root from the test output directory.");
    }
}
