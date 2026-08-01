#nullable enable

using System;
using System.Linq;
using Mutagen.Bethesda.Plugins.Aspects;
using Mutagen.Bethesda.Plugins.Records;
using Xunit;

namespace FormID_Database_Manager.Tests.Unit.Architecture;

/// <summary>
///     Holds the two facts about Mutagen that let Entry Extraction choose a display name with a single interface cast
///     instead of reflecting over each record's runtime type.
/// </summary>
/// <remarks>
///     <para>
///         Entry Extraction used to discover names reflectively, on the theory that a cast to one aspect interface
///         could not reach every named record type. It could not deliver on that theory — it searched for an interface
///         whose name contained <c>INamedGetter</c>, found only <c>INamedGetter</c> itself, and then asked that
///         aspect's plain <see cref="string" /> <c>Name</c> for a nested <c>String</c> property it does not have, so
///         the tier always returned nothing (issue #51).
///     </para>
///     <para>
///         ADR-0005 replaced it with a cast to <see cref="INamedRequiredGetter" />. These tests are why that cast is
///         sufficient and why it is not merely the old cast renamed. They assert facts about a third-party library
///         rather than about this application's code on purpose: if a Mutagen upgrade invalidates either, the single
///         cast stops being the right shape, and the failure should name the reason rather than surface as a handful
///         of records quietly losing their names.
///     </para>
/// </remarks>
public sealed class MutagenNameAspectTests
{
    /// <summary>
    ///     Verifies every aspect interface Mutagen uses to expose a record's display name derives from
    ///     <see cref="INamedRequiredGetter" />, so one cast to it reaches every named record type.
    /// </summary>
    /// <remarks>
    ///     Discovered from the aspects namespace rather than listed, so an aspect a Mutagen upgrade adds is covered
    ///     without an edit here — that is the point of asserting the rule over the discovered set rather than checking
    ///     four known types. The containment assertion is only there to keep the discovery honest: a namespace rename
    ///     or a filter that stops matching would otherwise make this pass over an empty set.
    /// </remarks>
    [Fact]
    public void EveryMutagenNameAspect_DerivesFromINamedRequiredGetter()
    {
        var nameAspects = typeof(INamedRequiredGetter).Assembly
            .GetExportedTypes()
            .Where(type => type.IsInterface)
            .Where(type => type.Namespace == typeof(INamedRequiredGetter).Namespace)
            .Where(type => type.Name.Contains("Named", StringComparison.Ordinal) &&
                           type.Name.EndsWith("Getter", StringComparison.Ordinal))
            .ToArray();

        Type[] knownNameAspects =
        [
            typeof(INamedGetter),
            typeof(INamedRequiredGetter),
            typeof(ITranslatedNamedGetter),
            typeof(ITranslatedNamedRequiredGetter)
        ];
        Assert.All(knownNameAspects, known => Assert.Contains(known, nameAspects));
        Assert.All(nameAspects, aspect => Assert.True(
            typeof(INamedRequiredGetter).IsAssignableFrom(aspect),
            $"{aspect.Name} exposes a display name but does not derive from {nameof(INamedRequiredGetter)}, so Entry " +
            "Extraction's single cast no longer reaches every named record type."));
    }

    /// <summary>
    ///     Verifies the required aspect is genuinely wider than the optional one, by naming record types that carry a
    ///     display name reachable only through <see cref="INamedRequiredGetter" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This is the finding that made issue #51 a defect rather than dead code to delete. A survey of every
    ///         concrete record type across the four game assemblies found ten that implement the required aspect but
    ///         not the optional one — Skyrim's <c>Class</c>, <c>Key</c>, <c>Flora</c>, <c>Eyes</c> and
    ///         <c>CollisionLayer</c>, Fallout 4's <c>Key</c>, <c>Flora</c> and <c>CollisionLayer</c>, and Starfield's
    ///         <c>Planet</c> and <c>CollisionLayer</c>. Every one of those records without an EditorID was stored under
    ///         a synthesized label with its real name sitting unread.
    ///     </para>
    ///     <para>
    ///         Three named types rather than a re-run of the survey: a scan asserting "at least one such type exists"
    ///         would keep passing while the specific types this project's fixtures build moved out from under it.
    ///     </para>
    /// </remarks>
    [Theory]
    [InlineData(typeof(Mutagen.Bethesda.Skyrim.IClassGetter))]
    [InlineData(typeof(Mutagen.Bethesda.Fallout4.IKeyGetter))]
    [InlineData(typeof(Mutagen.Bethesda.Starfield.IPlanetGetter))]
    public void RecordTypesExist_WhoseNameIsReachableOnlyThroughTheRequiredAspect(Type recordType)
    {
        Assert.True(
            typeof(INamedRequiredGetter).IsAssignableFrom(recordType),
            $"{recordType.Name} was chosen as a required-named record type but no longer carries that aspect.");
        Assert.False(
            typeof(INamedGetter).IsAssignableFrom(recordType),
            $"{recordType.Name} now carries the optional name aspect too, so it no longer demonstrates why Entry " +
            $"Extraction casts to {nameof(INamedRequiredGetter)} rather than {nameof(INamedGetter)}.");
    }

    /// <summary>
    ///     Verifies Oblivion has no record type whose name only the required aspect reaches, which is why the Entry
    ///     Extraction theories covering this run over three game families rather than four.
    /// </summary>
    /// <remarks>
    ///     Asserted against Mutagen's Oblivion assembly rather than against this suite's fixture recipes, because the
    ///     claim being protected is one about Mutagen. A guard that read the fixture builder's own table could only
    ///     report that no Oblivion recipe was written — it could never notice Mutagen <em>gaining</em> an
    ///     Oblivion required-named type, which is the drift that would silently leave a family uncovered.
    /// </remarks>
    [Fact]
    public void Oblivion_HasNoRecordTypeReachableOnlyThroughTheRequiredAspect()
    {
        var requiredOnly = RequiredOnlyNamedRecordTypesIn(typeof(Mutagen.Bethesda.Oblivion.Npc).Assembly);

        Assert.True(
            requiredOnly.Length == 0,
            "Mutagen's Oblivion definitions now contain record types whose display name only " +
            $"{nameof(INamedRequiredGetter)} reaches: {string.Join(", ", requiredOnly)}. Oblivion is excluded from " +
            "the required-name theories on the grounds that it has none, so those theories now need it.");
    }

    /// <summary>
    ///     Verifies the other three families do have such record types, so the test above is asserting a real
    ///     difference between the families rather than a rule that happens to hold everywhere.
    /// </summary>
    [Theory]
    [InlineData(typeof(Mutagen.Bethesda.Skyrim.Npc))]
    [InlineData(typeof(Mutagen.Bethesda.Fallout4.Npc))]
    [InlineData(typeof(Mutagen.Bethesda.Starfield.Npc))]
    public void EveryOtherFamily_HasRecordTypesReachableOnlyThroughTheRequiredAspect(Type recordTypeInAssembly)
    {
        Assert.NotEmpty(RequiredOnlyNamedRecordTypesIn(recordTypeInAssembly.Assembly));
    }

    /// <summary>
    ///     Names the concrete record types in one game assembly whose display name only the required aspect reaches.
    /// </summary>
    /// <param name="assembly">The Mutagen game assembly to scan.</param>
    /// <returns>The distinct record type names, in ordinal order.</returns>
    /// <remarks>
    ///     Scans all types rather than exported ones: Mutagen's binary overlay classes are internal, and they are the
    ///     types a Processing Run actually reads, so leaving them out would survey the wrong half of the library.
    /// </remarks>
    private static string[] RequiredOnlyNamedRecordTypesIn(System.Reflection.Assembly assembly)
    {
        return assembly.GetTypes()
            .Where(type => type is { IsInterface: false, IsAbstract: false })
            .Where(type => typeof(IMajorRecordGetter).IsAssignableFrom(type))
            .Where(type => typeof(INamedRequiredGetter).IsAssignableFrom(type) &&
                           !typeof(INamedGetter).IsAssignableFrom(type))
            .Select(type => type.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }
}
