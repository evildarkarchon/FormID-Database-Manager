namespace FormID_Database_Manager.Services;

internal enum GameContextTransitionSource
{
    SelectedGameReleaseChanged,
    SelectedDetectedDirectoryChanged,
    AdvancedModeChanged,
    BrowsedDirectorySelected
}

/// <summary>
/// Describes the source of a Game Context change without exposing UI event handlers as workflow interface methods.
/// </summary>
internal readonly record struct GameContextTransition
{
    private GameContextTransition(GameContextTransitionSource source, string? browsedDirectoryPath)
    {
        Source = source;
        BrowsedDirectoryPath = browsedDirectoryPath;
    }

    public GameContextTransitionSource Source { get; }

    public string? BrowsedDirectoryPath { get; }

    /// <summary>
    /// Creates a transition for a selected GameRelease change.
    /// </summary>
    public static GameContextTransition SelectedGameReleaseChanged()
    {
        return new GameContextTransition(GameContextTransitionSource.SelectedGameReleaseChanged, null);
    }

    /// <summary>
    /// Creates a transition for changing among detected installed directories.
    /// </summary>
    public static GameContextTransition SelectedDetectedDirectoryChanged()
    {
        return new GameContextTransition(GameContextTransitionSource.SelectedDetectedDirectoryChanged, null);
    }

    /// <summary>
    /// Creates a transition for an Advanced Mode change.
    /// </summary>
    public static GameContextTransition AdvancedModeChanged()
    {
        return new GameContextTransition(GameContextTransitionSource.AdvancedModeChanged, null);
    }

    /// <summary>
    /// Creates a transition for a directory returned by the Browse picker.
    /// </summary>
    /// <param name="directoryPath">The selected game root or Data directory.</param>
    public static GameContextTransition BrowsedDirectorySelected(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        return new GameContextTransition(GameContextTransitionSource.BrowsedDirectorySelected, directoryPath);
    }
}
