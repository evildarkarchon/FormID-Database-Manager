using Mutagen.Bethesda;

namespace FormID_Database_Manager.Services;

public interface IGameLoadOrderProvider
{
    GameLoadOrderSnapshot BuildSnapshot(
        GameRelease gameRelease,
        string dataPath,
        bool includeMasterFlagsLookup = false);
}
