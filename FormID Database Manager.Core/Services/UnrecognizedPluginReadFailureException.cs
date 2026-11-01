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
///     Mutagen rather than a fact about the Plugin's data. Its message is the classified cause's diagnostic text, found
///     behind Mutagen's enrichment wrappers, while the original failure is kept whole as the inner exception (ADR-0016
///     rule 6); the sentence the user reads is <see cref="ProcessingRunPresentation" />'s (ADR-0012).
/// </remarks>
internal sealed class UnrecognizedPluginReadFailureException : Exception
{
    /// <summary>
    ///     Creates the run-level failure for one unrecognised Plugin-read failure.
    /// </summary>
    /// <param name="pluginName">The selected Plugin that was being read.</param>
    /// <param name="phase">The Plugin-read phase the failure arrived in.</param>
    /// <param name="diagnosticMessage">
    ///     The classified cause's message, which becomes this exception's message. It differs from
    ///     <paramref name="innerException" />'s own message when an enrichment wrapper carries wording of its own.
    /// </param>
    /// <param name="innerException">The original failure, kept whole for diagnostics.</param>
    /// <exception cref="ArgumentException"><paramref name="pluginName" /> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="phase" /> is not defined.</exception>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="diagnosticMessage" /> or <paramref name="innerException" /> is null.
    /// </exception>
    public UnrecognizedPluginReadFailureException(
        string pluginName,
        PluginReadPhase phase,
        string diagnosticMessage,
        Exception innerException)
        : base(diagnosticMessage, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginName);
        PluginReadPhaseGuard.ThrowIfUndefined(phase);
        // Checked here because Exception's constructor accepts a null message and substitutes its own default text,
        // which would put a sentence about nothing in the place of the cause.
        ArgumentNullException.ThrowIfNull(diagnosticMessage);
        ArgumentNullException.ThrowIfNull(innerException);

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
}
