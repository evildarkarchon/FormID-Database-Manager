using System.Collections.Generic;
using System.Linq;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Environments;

namespace FormID_Database_Manager.Services;

public sealed class GameLoadOrderProvider : IGameLoadOrderProvider
{
    public IReadOnlyList<string> GetListedPluginNames(GameRelease gameRelease, string dataPath)
    {
        using var env = GameEnvironment.Typical.Builder(gameRelease)
            .WithTargetDataFolder(dataPath)
            .Build();

        return env.LoadOrder.ListedOrder
            .Select(listing => listing.ModKey.FileName.ToString())
            .ToList();
    }
}
