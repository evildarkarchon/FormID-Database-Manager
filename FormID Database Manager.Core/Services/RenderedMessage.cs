namespace FormID_Database_Manager.Services;

/// <summary>
///     One message a renderer has worded and assigned to a Message List, but not yet written anywhere.
/// </summary>
/// <param name="List">The Message List the message belongs to, chosen by the renderer.</param>
/// <param name="Text">The sentence the user reads.</param>
/// <remarks>
///     This is the one reporting convention for every renderer that writes the Message Lists (ADR-0014): the renderer
///     decides both the wording and the list, and whoever writes the message passes <see cref="List" /> straight to
///     <see cref="ViewModels.MainWindowViewModel.AddMessage" /> without a per-list switch. It lives beside
///     <see cref="MessageList" /> so no renderer needs a type of its own for the same pair.
/// </remarks>
internal readonly record struct RenderedMessage(MessageList List, string Text);
