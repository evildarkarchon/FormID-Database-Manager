namespace FormID_Database_Manager.Services;

/// <summary>
///     The failure raised when reading a selected Plugin fails for a reason the application does not recognise. It
///     fails the whole Processing Run and names the Plugin being read, so the user can deselect that Plugin and run
///     the rest (ADR-0016 rule 7).
/// </summary>
/// <remarks>
///     Modelled on <see cref="UnresolvableMasterException" />: it leaves the run as an exception rather than a
///     Processing Run Outcome (ADR-0007). It is deliberately not a <see cref="PluginReadException" />, because Plugin
///     Ingestion turns those into Failed Plugins, and an unrecognised cause may be a defect in the application or in
///     Mutagen rather than a fact about the Plugin's data. Its message is the original exception's diagnostic text;
///     the sentence the user reads is <see cref="ProcessingRunPresentation" />'s (ADR-0012).
/// </remarks>
internal sealed class UnrecognizedPluginReadFailureException : Exception
{
    /// <summary>
    ///     Creates the run-level failure for one unrecognised Plugin-read failure.
    /// </summary>
    /// <param name="pluginName">The selected Plugin that was being read.</param>
    /// <param name="phase">The Plugin-read phase the failure arrived in.</param>
    /// <param name="innerException">The original failure, kept for diagnostics and as the source of the message.</param>
    /// <exception cref="ArgumentException"><paramref name="pluginName" /> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="phase" /> is not defined.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="innerException" /> is null.</exception>
    public UnrecognizedPluginReadFailureException(
        string pluginName,
        PluginReadPhase phase,
        Exception innerException)
        // Validated inside the base initializer, as UnresolvableMasterException does, because the constructor body runs
        // only after base() has already read the inner exception's message.
        : base(EnsureInnerException(innerException).Message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginName);
        if (!Enum.IsDefined(phase))
        {
            throw new ArgumentOutOfRangeException(nameof(phase), phase, "Unsupported Plugin-read phase.");
        }

        PluginName = pluginName;
        Phase = phase;
    }

    /// <summary>
    ///     The selected Plugin that was being read when the failure arrived.
    /// </summary>
    public string PluginName { get; }

    /// <summary>
    ///     The Plugin-read phase the failure arrived in.
    /// </summary>
    public PluginReadPhase Phase { get; }

    /// <summary>
    ///     Validates the original failure and returns it, so the guard can run inside the base initializer.
    /// </summary>
    /// <param name="innerException">The original failure.</param>
    /// <returns>The same failure.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="innerException" /> is null.</exception>
    private static Exception EnsureInnerException(Exception innerException)
    {
        ArgumentNullException.ThrowIfNull(innerException);
        return innerException;
    }
}
