using FormID_Database_Manager.Services;

namespace FormID_Database_Manager.TestUtilities.Builders;

public static class GameLoadOrderSnapshotFactory
{
    public static GameLoadOrderSnapshot CreateSnapshot(params string[] pluginNames)
    {
        return new GameLoadOrderSnapshot(pluginNames);
    }
}
