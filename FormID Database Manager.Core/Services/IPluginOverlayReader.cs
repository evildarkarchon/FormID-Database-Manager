using Mutagen.Bethesda;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Oblivion;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Starfield;

namespace FormID_Database_Manager.Services;

internal interface IPluginOverlayReader
{
    IModDisposeGetter ReadOverlay(
        string pluginPath,
        GameRelease gameRelease,
        BinaryReadParameters readParameters);
}

internal sealed class MutagenPluginOverlayReader : IPluginOverlayReader
{
    public IModDisposeGetter ReadOverlay(
        string pluginPath,
        GameRelease gameRelease,
        BinaryReadParameters readParameters)
    {
        return gameRelease switch
        {
            GameRelease.Oblivion => OblivionMod.CreateFromBinaryOverlay(pluginPath,
                OblivionRelease.Oblivion,
                readParameters),
            GameRelease.SkyrimLE => SkyrimMod.CreateFromBinaryOverlay(pluginPath,
                SkyrimRelease.SkyrimLE,
                readParameters),
            GameRelease.SkyrimSE => SkyrimMod.CreateFromBinaryOverlay(pluginPath,
                SkyrimRelease.SkyrimSE,
                readParameters),
            GameRelease.SkyrimSEGog => SkyrimMod.CreateFromBinaryOverlay(pluginPath,
                SkyrimRelease.SkyrimSEGog,
                readParameters),
            GameRelease.SkyrimVR => SkyrimMod.CreateFromBinaryOverlay(pluginPath,
                SkyrimRelease.SkyrimVR,
                readParameters),
            GameRelease.EnderalLE => SkyrimMod.CreateFromBinaryOverlay(pluginPath,
                SkyrimRelease.EnderalLE,
                readParameters),
            GameRelease.EnderalSE => SkyrimMod.CreateFromBinaryOverlay(pluginPath,
                SkyrimRelease.EnderalSE,
                readParameters),
            GameRelease.Fallout4 => Fallout4Mod.CreateFromBinaryOverlay(pluginPath,
                Fallout4Release.Fallout4,
                readParameters),
            GameRelease.Fallout4VR => Fallout4Mod.CreateFromBinaryOverlay(pluginPath,
                Fallout4Release.Fallout4VR,
                readParameters),
            GameRelease.Starfield => StarfieldMod.CreateFromBinaryOverlay(pluginPath,
                StarfieldRelease.Starfield,
                readParameters),
            _ => throw new NotSupportedException($"Unsupported game release: {gameRelease}")
        };
    }
}
