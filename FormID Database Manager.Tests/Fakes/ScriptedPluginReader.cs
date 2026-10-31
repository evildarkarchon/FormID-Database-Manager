using FormID_Database_Manager.Services;

namespace FormID_Database_Manager.Tests.Fakes;

/// <summary>
///     A Plugin reader whose Plugins speak only the seam's domain: each opened Plugin yields one FormID record, after
///     reporting any scripted record issues, unless a scenario scripts an opening, reading, or disposal failure for it.
///     It records every opening and disposal in order so Plugin Ingestion's tests can assert on them.
/// </summary>
/// <remarks>
///     Every classification of a Mutagen failure lives behind the seam in <see cref="MutagenPluginReader" />, so a
///     scenario here throws the already-classified result (<see cref="PluginReadException" />,
///     <see cref="UnresolvableMasterException" />) or an unclassified failure that must propagate unchanged.
/// </remarks>
/// <param name="events">
///     An optional shared event log; each opening appends <c>open:&lt;Plugin name&gt;</c> so a scenario can assert how
///     opening interleaves with load-order preparation, progress, and Store writes.
/// </param>
internal sealed class ScriptedPluginReader(List<string>? events = null) : IPluginReader
{
    /// <summary>
    ///     Gets every ready case presented to <see cref="Open" />, in order, including ones whose opening then failed.
    /// </summary>
    public List<SelectedPluginReady> OpenedCases { get; } = [];

    /// <summary>
    ///     Gets the names of every Plugin presented to <see cref="Open" />, in order.
    /// </summary>
    public List<string> OpenedPlugins => OpenedCases.ConvertAll(readyPlugin => readyPlugin.PluginName);

    /// <summary>
    ///     Gets the names of every opened Plugin that was disposed, in order, recorded before any disposal failure.
    /// </summary>
    public List<string> DisposedPlugins { get; } = [];

    /// <summary>
    ///     Gets the failure to raise from <see cref="Open" /> instead of opening, keyed by Plugin name.
    /// </summary>
    public Dictionary<string, Exception> OpeningFailures { get; } = new(StringComparer.Ordinal);

    /// <summary>
    ///     Gets the failure to raise while reading, after the Plugin's one record was yielded, keyed by Plugin name.
    /// </summary>
    public Dictionary<string, Exception> ReadingFailures { get; } = new(StringComparer.Ordinal);

    /// <summary>
    ///     Gets the failure to raise while disposing an opened Plugin, keyed by Plugin name.
    /// </summary>
    public Dictionary<string, Exception> DisposalFailures { get; } = new(StringComparer.Ordinal);

    /// <summary>
    ///     Gets the record issues to report through the callback before the Plugin's one record, keyed by Plugin name.
    /// </summary>
    public Dictionary<string, IReadOnlyList<string>> RecordIssues { get; } = new(StringComparer.Ordinal);

    /// <summary>
    ///     Gets the names of Plugins that open cleanly but yield no FormID records.
    /// </summary>
    public HashSet<string> EmptyPlugins { get; } = new(StringComparer.Ordinal);

    /// <summary>
    ///     Gets a value indicating whether reading any record fails loudly, for callers defined never to read one.
    /// </summary>
    public bool RejectsReading { get; init; }

    /// <summary>
    ///     Runs after a Plugin has been recorded as opened and before any scripted opening failure is raised.
    /// </summary>
    public Action<string>? OnOpened { get; init; }

    /// <summary>
    ///     Runs during disposal, after the disposal has been recorded and before any scripted disposal failure.
    /// </summary>
    public Action<string>? OnDisposed { get; init; }

    /// <inheritdoc />
    public IOpenedPlugin Open(SelectedPluginReady readyPlugin)
    {
        ArgumentNullException.ThrowIfNull(readyPlugin);
        var pluginName = readyPlugin.PluginName;
        OpenedCases.Add(readyPlugin);
        events?.Add($"open:{pluginName}");
        OnOpened?.Invoke(pluginName);
        if (OpeningFailures.TryGetValue(pluginName, out var openingFailure))
        {
            throw openingFailure;
        }

        return new ScriptedOpenedPlugin(this, pluginName);
    }

    /// <summary>
    ///     One opened Plugin whose reading and disposal follow its reader's script.
    /// </summary>
    /// <param name="reader">The reader holding the script and the recorded observations.</param>
    /// <param name="pluginName">The selected Plugin this instance was opened for.</param>
    private sealed class ScriptedOpenedPlugin(ScriptedPluginReader reader, string pluginName) : IOpenedPlugin
    {
        /// <inheritdoc />
        public IEnumerable<FormIdRecord> ReadRecords(
            Action<string> reportRecordIssue,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(reportRecordIssue);
            return Read(reportRecordIssue, cancellationToken);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            reader.DisposedPlugins.Add(pluginName);
            reader.OnDisposed?.Invoke(pluginName);
            if (reader.DisposalFailures.TryGetValue(pluginName, out var disposalFailure))
            {
                throw disposalFailure;
            }
        }

        /// <summary>
        ///     Lazily reports the scripted issues, yields the Plugin's one record, and then raises any reading failure,
        ///     checking the token per record as the production reader does.
        /// </summary>
        private IEnumerable<FormIdRecord> Read(
            Action<string> reportRecordIssue,
            CancellationToken cancellationToken)
        {
            if (reader.RejectsReading)
            {
                throw new InvalidOperationException("This caller must not read any Plugin record.");
            }

            if (reader.RecordIssues.TryGetValue(pluginName, out var issues))
            {
                foreach (var issue in issues)
                {
                    reportRecordIssue(issue);
                }
            }

            if (!reader.EmptyPlugins.Contains(pluginName))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new FormIdRecord("000800", $"NPC_{Path.GetFileNameWithoutExtension(pluginName)}");
            }

            if (reader.ReadingFailures.TryGetValue(pluginName, out var readingFailure))
            {
                throw readingFailure;
            }
        }
    }
}
