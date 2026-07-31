using Mutagen.Bethesda;

namespace FormID_Database_Manager.Services;

/// <summary>
/// Locates game installations using Mutagen's GameLocations API.
/// Wraps the static API behind an interface for testability.
/// </summary>
/// <remarks>
/// The lookup itself now lives in <see cref="GameInstallationProbe" />, so there is one implementation of it rather
/// than two identical ones in Core. This type survives only as the interface UserWorkflow still takes; ADR-0002
/// retires it once install location moves onto the Game Installation module.
/// </remarks>
public class GameLocationService : IGameLocationService
{
    private readonly GameInstallationProbe _probe = new();

    public List<string> GetGameFolders(GameRelease release)
    {
        return [.._probe.GetInstalledDirectories(release)];
    }
}
