namespace FormID_Database_Manager.Services;

/// <summary>
///     Reports a Failed Plugin from either read phase: opening the Plugin, or reading its records.
/// </summary>
/// <remarks>
///     It stays an exception rather than a result value because the Store consumes the record stream inside its atomic
///     Plugin write, and an exception thrown through that write is what rolls the half-read Plugin back. Only the
///     application raises it, which is what keeps arbitrary adapter failures from becoming Failed Plugins.
/// </remarks>
/// <param name="phase">The read phase that failed.</param>
/// <param name="diagnosticMessage">The underlying Plugin-read message reported on the Failed Plugin.</param>
/// <param name="innerException">The Mutagen or filesystem exception retained for diagnostics.</param>
internal sealed class PluginReadException(
    PluginReadPhase phase,
    string diagnosticMessage,
    Exception? innerException = null)
    : Exception(diagnosticMessage, innerException)
{
    /// <summary>
    ///     The read phase that failed.
    /// </summary>
    public PluginReadPhase Phase { get; } = phase;

    /// <summary>
    ///     The underlying Plugin-read message, retained as diagnostics rather than presentation wording.
    /// </summary>
    public string DiagnosticMessage { get; } = diagnosticMessage;
}
