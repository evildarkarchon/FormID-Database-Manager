namespace FormID_Database_Manager.Services;

/// <summary>
///     Names one of the three Message Lists the Main Window shows: errors, warnings and information.
/// </summary>
/// <remarks>
///     One shared name for a list, so a renderer can choose the list a message belongs to and every writer can pass
///     that choice straight to <see cref="ViewModels.MainWindowViewModel.AddMessage" /> without a per-list switch
///     (ADR-0014).
///     It lives in Services rather than beside the ViewModel so that renderers name a list without importing the
///     ViewModels namespace.
/// </remarks>
internal enum MessageList
{
    /// <summary>The error list: something failed or was refused.</summary>
    Error,

    /// <summary>The warning list: something went wrong without stopping the work, like a Processing Warning.</summary>
    Warning,

    /// <summary>The information list: guidance or a result that is not a failure.</summary>
    Information
}
