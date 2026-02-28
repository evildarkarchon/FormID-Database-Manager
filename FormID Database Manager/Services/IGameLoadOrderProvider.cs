using System.Collections.Generic;
using Mutagen.Bethesda;

namespace FormID_Database_Manager.Services;

public interface IGameLoadOrderProvider
{
    IReadOnlyList<string> GetListedPluginNames(GameRelease gameRelease, string dataPath);
}
