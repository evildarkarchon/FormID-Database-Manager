namespace FormID_Database_Manager.Services;

/// <summary>
///     Renders everything Game Context says to the user: why a pick was refused, or why nothing was found.
/// </summary>
/// <remarks>
///     <para>
///         This is a pure renderer in the same role as <see cref="ProcessingRunPresentation" />: it takes a typed
///         <see cref="GameContextNotice" />, returns its wording and the message list it belongs to, writes nothing,
///         and holds no state (ADR-0013). Which list a notice goes to is a presentation choice, so it sits here beside
///         the wording rather than with whoever writes the list.
///     </para>
///     <para>
///         Every Game Context sentence lives here, word for word as Game Context used to build it, and is asserted only
///         by <c>GameContextPresentationTests</c>.
///     </para>
/// </remarks>
internal static class GameContextPresentation
{
    /// <summary>
    ///     Renders one Game Context notice into the message its caller should show and the list it belongs to.
    /// </summary>
    /// <param name="notice">What Game Context reported.</param>
    /// <returns>
    ///     The message list the notice belongs to, and its text. Game Context never warns, so the list is only ever
    ///     <see cref="MessageList.Error" /> or <see cref="MessageList.Information" />.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="notice" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The notice is not one this module knows how to render.</exception>
    internal static RenderedMessage Render(GameContextNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);

        return notice switch
        {
            GameDirectoryPickerFailed pickerFailed => new RenderedMessage(
                MessageList.Error,
                $"Error selecting game directory: {pickerFailed.ErrorMessage}"),
            // No installed location is not a failure: the user can still Browse, so this is guidance, not an error.
            NoInstalledLocationsFound noLocations => new RenderedMessage(
                MessageList.Information,
                $"No installed locations found for {noLocations.GameRelease}. Use Browse to select a directory."),
            DirectoryRefused refused => new RenderedMessage(
                MessageList.Error,
                $"Could not read the selected directory: {refused.FailureMessage}"),
            NoGameDetected => new RenderedMessage(
                MessageList.Error,
                "Could not detect game from directory. Please select a game from the dropdown."),
            _ => throw new ArgumentOutOfRangeException(
                nameof(notice),
                notice,
                "Unsupported Game Context notice.")
        };
    }
}
