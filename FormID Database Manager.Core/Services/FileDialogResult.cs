namespace FormID_Database_Manager.Services;

/// <summary>
/// Identifies the outcome of a UI-neutral file or folder picker operation.
/// </summary>
public enum FileDialogResultKind
{
    /// <summary>
    /// The user selected a path.
    /// </summary>
    Success,

    /// <summary>
    /// The user dismissed the picker without selecting a path.
    /// </summary>
    Cancelled,

    /// <summary>
    /// The picker failed before it could produce a user choice.
    /// </summary>
    Failure
}

/// <summary>
/// Carries picker outcome facts without forcing platform adapters to mutate workflow state.
/// </summary>
public sealed record FileDialogResult
{
    private FileDialogResult(FileDialogResultKind kind, string? path, string? errorMessage)
    {
        Kind = kind;
        Path = path;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// Gets the picker outcome category.
    /// </summary>
    public FileDialogResultKind Kind { get; }

    /// <summary>
    /// Gets the selected path when <see cref="Kind" /> is <see cref="FileDialogResultKind.Success" />.
    /// </summary>
    public string? Path { get; }

    /// <summary>
    /// Gets the failure reason when <see cref="Kind" /> is <see cref="FileDialogResultKind.Failure" />.
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// Creates a successful picker result.
    /// </summary>
    /// <param name="path">The selected path.</param>
    /// <returns>A successful picker result containing <paramref name="path" />.</returns>
    public static FileDialogResult Success(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new FileDialogResult(FileDialogResultKind.Success, path, null);
    }

    /// <summary>
    /// Creates a cancelled picker result.
    /// </summary>
    /// <returns>A cancelled picker result.</returns>
    public static FileDialogResult Cancelled()
    {
        return new FileDialogResult(FileDialogResultKind.Cancelled, null, null);
    }

    /// <summary>
    /// Creates a failed picker result.
    /// </summary>
    /// <param name="errorMessage">The platform picker failure message.</param>
    /// <returns>A failed picker result containing <paramref name="errorMessage" />.</returns>
    public static FileDialogResult Failure(string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new FileDialogResult(FileDialogResultKind.Failure, null, errorMessage);
    }
}
