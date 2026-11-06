using System.Collections.Immutable;

namespace FormID_Database_Manager.Services;

internal enum AdvancedMode
{
    Off,
    On
}

/// <summary>
///     Describes one immutable Plugin entry in Plugin List order.
/// </summary>
/// <param name="Name">The Plugin name using the casing reported by discovery.</param>
internal sealed record PluginListEntry(string Name);

/// <summary>
///     Captures one confirmed membership and selection snapshot for a Plugin List Source.
/// </summary>
internal sealed record ConfirmedPluginList(
    long MembershipVersion,
    PluginListSource Source,
    AdvancedMode AdvancedMode,
    ImmutableArray<PluginListEntry> Entries,
    ImmutableArray<string> SelectedPluginNames);

/// <summary>
///     The Plugin List confirmation: what one Plugin List snapshot can say about a given Plugin List Source. It holds a
///     Confirmed Plugin List for that source, is still loading one, or cannot supply one.
/// </summary>
/// <remarks>
///     Derivation is closed to this assembly, as it is for the admission decision, so the three cases below are the only
///     ones a consumer has to handle. The cases use Plugin List vocabulary only; a consumer such as Processing Run
///     Admission maps them to its own reasons.
/// </remarks>
internal abstract record PluginListConfirmation
{
    private protected PluginListConfirmation()
    {
    }
}

/// <summary>
///     The snapshot holds a Confirmed Plugin List for the asked-about Plugin List Source.
/// </summary>
internal sealed record ConfirmedPluginListAvailable : PluginListConfirmation
{
    /// <summary>
    ///     Initializes the available case from the matching Confirmed Plugin List.
    /// </summary>
    /// <param name="confirmed">The Confirmed Plugin List whose source matched.</param>
    /// <exception cref="ArgumentNullException"><paramref name="confirmed" /> is null.</exception>
    internal ConfirmedPluginListAvailable(ConfirmedPluginList confirmed)
    {
        Confirmed = confirmed ?? throw new ArgumentNullException(nameof(confirmed));
    }

    /// <summary>
    ///     Gets the Confirmed Plugin List for the asked-about source, with its own Data-directory spelling.
    /// </summary>
    public ConfirmedPluginList Confirmed { get; }
}

/// <summary>
///     The snapshot holds no Confirmed Plugin List for the asked-about source, but one is on its way: discovery is in
///     flight, or the list still describes another source because its refresh has not started.
/// </summary>
internal sealed record ConfirmedPluginListLoading : PluginListConfirmation
{
    private ConfirmedPluginListLoading()
    {
    }

    /// <summary>
    ///     Gets the only instance of the loading case.
    /// </summary>
    public static ConfirmedPluginListLoading Instance { get; } = new();
}

/// <summary>
///     The snapshot cannot supply a Confirmed Plugin List for the asked-about source: there is no source, or that
///     source's discovery ended without a confirmation, and only a retry brings one.
/// </summary>
internal sealed record ConfirmedPluginListUnavailable : PluginListConfirmation
{
    private ConfirmedPluginListUnavailable()
    {
    }

    /// <summary>
    ///     Gets the only instance of the unavailable case.
    /// </summary>
    public static ConfirmedPluginListUnavailable Instance { get; } = new();
}

/// <summary>
///     Provides one immutable, coherent view of Plugin List membership, selection, and current activity.
/// </summary>
internal abstract record PluginListState
{
    /// <summary>
    ///     Initializes the common revision and confirmation contract for one published state.
    /// </summary>
    /// <param name="stateRevision">The revision of this complete published snapshot.</param>
    /// <param name="activityRevision">The revision of the most recent genuine activity occurrence.</param>
    /// <param name="confirmed">The optional confirmed Plugin List membership and selection.</param>
    protected PluginListState(
        long stateRevision,
        long activityRevision,
        ConfirmedPluginList? confirmed)
    {
        StateRevision = stateRevision;
        ActivityRevision = activityRevision;
        Confirmed = confirmed;
    }

    public long StateRevision { get; }

    public long ActivityRevision { get; }

    public virtual ConfirmedPluginList? Confirmed { get; }

    /// <summary>
    ///     Reports whether this snapshot is an expected discovery failure for exactly the given source.
    /// </summary>
    /// <param name="source">The Plugin List Source to ask about.</param>
    /// <returns>
    ///     True only for a Failed state governed by an equal source; every other variant, including Cancelled and
    ///     Faulted, reports false.
    /// </returns>
    /// <remarks>
    ///     The answer comes from this one snapshot, so a caller that reads <see cref="PluginList.Current" /> once gets
    ///     a coherent answer (ADR-0009). The Game Context asks this to decide whether a Browse to the same source is an
    ///     explicit retry.
    /// </remarks>
    public virtual bool HasFailedFor(PluginListSource source)
    {
        return false;
    }

    /// <summary>
    ///     Answers the Plugin List confirmation for the given source from this one snapshot.
    /// </summary>
    /// <param name="source">The Plugin List Source to ask about.</param>
    /// <returns>
    ///     Available when this snapshot's Confirmed Plugin List belongs to <paramref name="source" />, whatever the state
    ///     kind, so a refresh, failure, cancellation, or fault of the same source keeps the retained list usable.
    ///     Otherwise loading while discovery is in flight or while the snapshot still describes another source; otherwise
    ///     unavailable, because there is no source or the source's discovery ended without a confirmation.
    /// </returns>
    /// <remarks>
    ///     The answer comes from this one snapshot, so a caller that reads <see cref="PluginList.Current" /> once gets a
    ///     coherent answer (ADR-0009). It answers a different question from <see cref="HasFailedFor" />: a Failed state
    ///     that retained a same-source confirmation has failed for that source and still supplies its confirmation.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="source" /> is null.</exception>
    public PluginListConfirmation ConfirmationFor(PluginListSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (Confirmed is { } confirmed && confirmed.Source == source)
        {
            return new ConfirmedPluginListAvailable(confirmed);
        }

        // A list still describing another source means the Game Context moved on and the refresh for the asked-about
        // source has not started yet, so a list for it is on its way rather than unavailable.
        return this switch
        {
            PluginListRefreshingState => ConfirmedPluginListLoading.Instance,
            SourcedPluginListState sourced when sourced.Source != source => ConfirmedPluginListLoading.Instance,
            _ => ConfirmedPluginListUnavailable.Instance
        };
    }
}

/// <summary>
///     Represents the absence of a Plugin List Source and therefore of confirmed membership.
/// </summary>
internal sealed record PluginListNoSourceState : PluginListState
{
    /// <summary>
    ///     Initializes one no-source activity occurrence.
    /// </summary>
    /// <param name="stateRevision">The revision of this complete published snapshot.</param>
    /// <param name="activityRevision">The revision of this no-source occurrence.</param>
    internal PluginListNoSourceState(long stateRevision, long activityRevision)
        : base(stateRevision, activityRevision, null)
    {
    }
}

/// <summary>
///     Provides source ownership and confirmation coherence for every sourced Plugin List state.
/// </summary>
internal abstract record SourcedPluginListState : PluginListState
{
    /// <summary>
    ///     Initializes one sourced state and rejects confirmation retained from another source.
    /// </summary>
    /// <param name="stateRevision">The revision of this complete published snapshot.</param>
    /// <param name="activityRevision">The revision of the most recent genuine activity occurrence.</param>
    /// <param name="source">The Plugin List Source governing this state.</param>
    /// <param name="confirmed">Optional confirmed membership retained for the same source.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source" /> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="confirmed" /> belongs to another source.</exception>
    protected SourcedPluginListState(
        long stateRevision,
        long activityRevision,
        PluginListSource source,
        ConfirmedPluginList? confirmed)
        : base(stateRevision, activityRevision, confirmed)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (confirmed is not null && confirmed.Source != source)
        {
            throw new ArgumentException(
                "Confirmed Plugin List must belong to the sourced state.",
                nameof(confirmed));
        }

        Source = source;
    }

    public PluginListSource Source { get; }
}

/// <summary>
///     Represents discovery in progress for one Plugin List Source.
/// </summary>
internal sealed record PluginListRefreshingState : SourcedPluginListState
{
    /// <summary>
    ///     Initializes one refreshing activity occurrence with its latest discovery counts.
    /// </summary>
    /// <param name="stateRevision">The revision of this complete published snapshot.</param>
    /// <param name="activityRevision">The revision of this refreshing occurrence.</param>
    /// <param name="source">The Plugin List Source being refreshed.</param>
    /// <param name="confirmed">Optional confirmed membership retained for the same source.</param>
    /// <param name="scannedCount">The number of Plugins scanned so far.</param>
    /// <param name="totalCount">The total number of Plugins discovery expects to scan.</param>
    internal PluginListRefreshingState(
        long stateRevision,
        long activityRevision,
        PluginListSource source,
        ConfirmedPluginList? confirmed,
        int scannedCount,
        int totalCount)
        : base(stateRevision, activityRevision, source, confirmed)
    {
        ScannedCount = scannedCount;
        TotalCount = totalCount;
    }

    public int ScannedCount { get; }

    public int TotalCount { get; }
}

/// <summary>
///     Represents successfully confirmed Plugin List membership.
/// </summary>
internal sealed record PluginListReadyState : SourcedPluginListState
{
    /// <summary>
    ///     Initializes one Ready occurrence from its required confirmation.
    /// </summary>
    /// <param name="stateRevision">The revision of this complete published snapshot.</param>
    /// <param name="activityRevision">The revision of this Ready occurrence.</param>
    /// <param name="confirmed">The confirmed membership that owns the source and membership version.</param>
    /// <exception cref="ArgumentNullException"><paramref name="confirmed" /> is null.</exception>
    internal PluginListReadyState(
        long stateRevision,
        long activityRevision,
        ConfirmedPluginList confirmed)
        : base(
            stateRevision,
            activityRevision,
            (confirmed ?? throw new ArgumentNullException(nameof(confirmed))).Source,
            confirmed)
    {
        Confirmed = confirmed;
    }

    public override ConfirmedPluginList Confirmed { get; }

    public long MembershipVersion => Confirmed.MembershipVersion;
}

/// <summary>
///     Represents an expected local discovery failure for one Plugin List Source.
/// </summary>
internal sealed record PluginListFailedState : SourcedPluginListState
{
    /// <summary>
    ///     Initializes one expected failure occurrence with its UI-neutral detail.
    /// </summary>
    /// <param name="stateRevision">The revision of this complete published snapshot.</param>
    /// <param name="activityRevision">The revision of this failure occurrence.</param>
    /// <param name="source">The Plugin List Source whose discovery failed.</param>
    /// <param name="confirmed">Optional confirmed membership retained for the same source.</param>
    /// <param name="errorMessage">The UI-neutral expected failure detail.</param>
    internal PluginListFailedState(
        long stateRevision,
        long activityRevision,
        PluginListSource source,
        ConfirmedPluginList? confirmed,
        string errorMessage)
        : base(stateRevision, activityRevision, source, confirmed)
    {
        ErrorMessage = errorMessage;
    }

    public string ErrorMessage { get; }

    /// <summary>
    ///     Reports whether this failure belongs to the given source.
    /// </summary>
    /// <param name="source">The Plugin List Source to ask about.</param>
    /// <returns>True when <paramref name="source" /> equals the source whose discovery failed.</returns>
    public override bool HasFailedFor(PluginListSource source)
    {
        return Source == source;
    }
}

/// <summary>
///     Represents caller-requested cancellation of a refresh for one Plugin List Source.
/// </summary>
internal sealed record PluginListCancelledState : SourcedPluginListState
{
    /// <summary>
    ///     Initializes one cancellation occurrence with optional same-source confirmation.
    /// </summary>
    /// <param name="stateRevision">The revision of this complete published snapshot.</param>
    /// <param name="activityRevision">The revision of this cancellation occurrence.</param>
    /// <param name="source">The Plugin List Source whose refresh was cancelled.</param>
    /// <param name="confirmed">Optional confirmed membership retained for the same source.</param>
    internal PluginListCancelledState(
        long stateRevision,
        long activityRevision,
        PluginListSource source,
        ConfirmedPluginList? confirmed)
        : base(stateRevision, activityRevision, source, confirmed)
    {
    }
}

/// <summary>
///     Represents an unexpected refresh fault for one Plugin List Source.
/// </summary>
internal sealed record PluginListFaultedState : SourcedPluginListState
{
    /// <summary>
    ///     Initializes one fault occurrence with optional same-source confirmation.
    /// </summary>
    /// <param name="stateRevision">The revision of this complete published snapshot.</param>
    /// <param name="activityRevision">The revision of this fault occurrence.</param>
    /// <param name="source">The Plugin List Source whose refresh faulted.</param>
    /// <param name="confirmed">Optional confirmed membership retained for the same source.</param>
    internal PluginListFaultedState(
        long stateRevision,
        long activityRevision,
        PluginListSource source,
        ConfirmedPluginList? confirmed)
        : base(stateRevision, activityRevision, source, confirmed)
    {
    }
}

/// <summary>
///     Carries versioned intent to change one Plugin or the complete confirmed Plugin List.
/// </summary>
/// <param name="MembershipVersion">The membership version displayed when the intent originated.</param>
/// <param name="IsSelected">The desired selection state.</param>
internal abstract record PluginSelectionIntent(long MembershipVersion, bool IsSelected);

internal sealed record PluginSelectionByNameIntent(
    long MembershipVersion,
    string PluginName,
    bool IsSelected) : PluginSelectionIntent(MembershipVersion, IsSelected);

internal sealed record PluginSelectionForAllIntent(
    long MembershipVersion,
    bool IsSelected) : PluginSelectionIntent(MembershipVersion, IsSelected);
