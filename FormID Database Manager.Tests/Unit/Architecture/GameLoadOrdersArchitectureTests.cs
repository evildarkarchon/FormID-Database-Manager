using System.Reflection;
using System.Text.RegularExpressions;
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
            .SelectMany(type => ExpandPublicContractTypes(type, typeof(IGameLoadOrders).Assembly))
            .ToArray();
        var forbiddenTypes = new[]
        {
            typeof(bool),
            typeof(ILoadOrderListingGetter),
            typeof(IModMasterStyledGetter),
            typeof(BinaryReadParameters)
        };

        Assert.DoesNotContain(
            exposedTypes,
            exposedType => forbiddenTypes.Any(forbiddenType => forbiddenType.IsAssignableFrom(exposedType)));
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
            string.Concat("IPluginList", "Discovery"),
            string.Concat("PluginList", "Discovery", "Progress"),
            string.Concat("PluginList", "Discovery", "Result"),
            string.Concat("PluginList", "Discovery", "Completed"),
            string.Concat("PluginList", "Discovery", "Failed")
        };

        Assert.Contains("IGameLoadOrders", pluginListSource, StringComparison.Ordinal);
        Assert.Contains("DiscoverAvailablePluginsAsync", pluginListSource, StringComparison.Ordinal);
        Assert.Contains("IGameLoadOrders", ingestionSource, StringComparison.Ordinal);
        Assert.Contains("PrepareSelectedPlugins", ingestionSource, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(
            servicesDirectory,
            string.Concat("PluginList", "Discovery", ".cs"))));
        Assert.All(
            retiredTypeNames,
            retiredTypeName => Assert.Null(typeof(IGameLoadOrders).Assembly.GetType(
                $"{typeof(IGameLoadOrders).Namespace}.{retiredTypeName}")));
    }

    /// <summary>
    ///     Pins the live Plugin read seam to one operation taking one intact ready selected-Plugin case, without
    ///     exposing binary preparation or any Mutagen type.
    /// </summary>
    [Fact]
    public void PluginReaderContract_LiveOperation_AcceptsOnlyPreparedReadyCase()
    {
        var contractOperation = Assert.Single(typeof(IPluginReader).GetMethods());
        var productionOperation = Assert.Single(typeof(MutagenPluginReader).GetMethods(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly));
        var contractParameter = Assert.Single(contractOperation.GetParameters());
        var productionParameter = Assert.Single(productionOperation.GetParameters());

        Assert.Equal(nameof(IPluginReader.Open), contractOperation.Name);
        Assert.Equal(nameof(IPluginReader.Open), productionOperation.Name);
        Assert.Equal(typeof(SelectedPluginReady), contractParameter.ParameterType);
        Assert.Equal(typeof(SelectedPluginReady), productionParameter.ParameterType);
        Assert.Equal(typeof(IOpenedPlugin), contractOperation.ReturnType);
    }

    /// <summary>
    ///     Pins the opened Plugin's read operation to application-owned FormID records, so records leave the Plugin
    ///     read seam as FormIDs and Entries and no Mutagen record type crosses it (ADR-0015).
    /// </summary>
    [Fact]
    public void OpenedPluginContract_ReadRecords_ReturnsFormIdRecords()
    {
        var readOperation = typeof(IOpenedPlugin).GetMethod(nameof(IOpenedPlugin.ReadRecords));

        Assert.NotNull(readOperation);
        Assert.Equal(typeof(IEnumerable<FormIdRecord>), readOperation.ReturnType);
    }

    /// <summary>
    ///     Pins one cohesive lower environment seam and prevents the retired delegate-bundle substitution shape.
    /// </summary>
    [Fact]
    public void GameLoadOrdersConstruction_RetiredDelegateBundle_DoesNotReappear()
    {
        var moduleConstructors = typeof(GameLoadOrders).GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var delegateParameters = moduleConstructors
            .Concat(typeof(GameLoadOrderEnvironment).GetConstructors(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            .SelectMany(constructor => constructor.GetParameters())
            .Where(parameter => typeof(Delegate).IsAssignableFrom(parameter.ParameterType))
            .ToArray();

        Assert.Empty(delegateParameters);
    }

    /// <summary>
    ///     Pins the production composition entry point that pairs the highest Game Load Orders seam with the production
    ///     Plugin reader without exposing either dependency to Processing Run, and pins Plugin Ingestion's widest
    ///     constructor to exactly Game Load Orders and the Plugin reader (ADR-0015).
    /// </summary>
    [Fact]
    public void PluginIngestionProductionComposition_HighestGameLoadOrdersSeam_HasMatchedEntryPoint()
    {
        var constructors = typeof(PluginIngestion).GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var parameterLists = constructors
            .Select(constructor => constructor.GetParameters().Select(parameter => parameter.ParameterType).ToArray())
            .ToArray();

        var widest = parameterLists.MaxBy(parameters => parameters.Length);

        Assert.Contains(parameterLists, parameters => parameters.SequenceEqual([typeof(IGameLoadOrders)]));
        Assert.Equal([typeof(IGameLoadOrders), typeof(IPluginReader)], widest);
    }

    /// <summary>
    ///     Prevents the retired provider, mixed snapshot, Boolean policy, and their test helpers from returning to
    ///     active source while leaving historical ADR discussion intact.
    /// </summary>
    [Fact]
    public void ActiveSources_FinalGameLoadOrdersArchitecture_ContainNoRetiredProtocols()
    {
        var repositoryRoot = FindRepositoryRoot();
        var activeSourceDirectories = new[]
        {
            Path.Combine(repositoryRoot, "FormID Database Manager.Core"),
            Path.Combine(repositoryRoot, "FormID Database Manager.WinUI"),
            Path.Combine(repositoryRoot, "FormID Database Manager.TestUtilities"),
            Path.Combine(repositoryRoot, "FormID Database Manager.Tests")
        };
        var retiredProtocolNames = new[]
        {
            string.Concat("IGameLoadOrder", "Provider"),
            string.Concat("GameLoadOrder", "Provider"),
            string.Concat("GameLoadOrder", "Snapshot"),
            string.Concat("GameLoadOrder", "Snapshot", "Factory"),
            string.Concat("StaticGameLoadOrder", "Provider"),
            string.Concat("PreparedGameLoad", "Orders"),
            string.Concat("Build", "Snapshot"),
            string.Concat("IPluginList", "Discovery"),
            string.Concat("PluginList", "Discovery", "Progress"),
            string.Concat("PluginList", "Discovery", "Result"),
            string.Concat("PluginList", "Discovery", "Completed"),
            string.Concat("PluginList", "Discovery", "Failed")
        };
        var retiredReferences = activeSourceDirectories
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            .Where(path => !IsBuildOutput(path))
            .Select(path => new { Path = path, Source = File.ReadAllText(path) })
            .Where(file => retiredProtocolNames.Any(retiredName => Regex.IsMatch(
                file.Source,
                $@"\b{Regex.Escape(retiredName)}\b",
                RegexOptions.CultureInvariant)))
            .Select(file => Path.GetRelativePath(repositoryRoot, file.Path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(retiredReferences);
    }

    /// <summary>
    ///     Keeps the Plugin reader, Mutagen record types, and FormID Record Store boundary types outside Game Load
    ///     Orders without pinning its private helpers or exact environment calls.
    /// </summary>
    [Fact]
    public void GameLoadOrdersSources_PreparationOwnership_ReferencesNoPluginReaderRecordOrStoreBoundaries()
    {
        var servicesDirectory = Path.Combine(FindRepositoryRoot(), "FormID Database Manager.Core", "Services");
        var source = string.Join(
            Environment.NewLine,
            File.ReadAllText(Path.Combine(servicesDirectory, "GameLoadOrders.cs")),
            File.ReadAllText(Path.Combine(servicesDirectory, "GameLoadOrderEnvironment.cs")));

        Assert.DoesNotContain("IPluginReader", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IOpenedPlugin", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MutagenPluginReader", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IModDisposeGetter", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IMajorRecordGetter", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IFormIdRecordStoreSession", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FormIdRecordStore", source, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Expands wrappers, application-owned public result properties, and dynamically discovered result cases so a
    ///     forbidden type cannot hide behind the highest seam without pinning those cases as records or classes.
    /// </summary>
    /// <param name="rootType">The role-operation type shape to expand.</param>
    /// <param name="applicationAssembly">The assembly whose application-owned result surfaces are traversed.</param>
    /// <returns>Every wrapper, result case, and public data type exposed through the role operation.</returns>
    private static IEnumerable<Type> ExpandPublicContractTypes(Type rootType, Assembly applicationAssembly)
    {
        var pending = new Stack<Type>();
        var visited = new HashSet<Type>();
        pending.Push(rootType);

        while (pending.TryPop(out var type))
        {
            if (!visited.Add(type))
            {
                continue;
            }

            yield return type;

            if (type.HasElementType && type.GetElementType() is { } elementType)
            {
                pending.Push(elementType);
            }

            foreach (var genericArgument in type.GetGenericArguments())
            {
                pending.Push(genericArgument);
            }

            if (type.Assembly != applicationAssembly)
            {
                continue;
            }

            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                pending.Push(property.PropertyType);
            }

            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                pending.Push(field.FieldType);
            }

            foreach (var resultCase in applicationAssembly.GetTypes()
                         .Where(candidate => candidate != type && type.IsAssignableFrom(candidate)))
            {
                pending.Push(resultCase);
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

    /// <summary>
    ///     Identifies generated source under build-output directories so architecture scans cover authored code only.
    /// </summary>
    /// <param name="path">The candidate source path.</param>
    /// <returns><see langword="true" /> when the path is under a bin or obj directory.</returns>
    private static bool IsBuildOutput(string path)
    {
        return path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                   StringComparison.OrdinalIgnoreCase)
               || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                   StringComparison.OrdinalIgnoreCase);
    }
}
