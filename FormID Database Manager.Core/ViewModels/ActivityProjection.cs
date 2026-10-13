namespace FormID_Database_Manager.ViewModels;

/// <summary>
///     One Workflow Activity's already-rendered report for the User Workflow's progress channel.
/// </summary>
/// <param name="IsActive">Whether the activity is currently running and therefore has something to say.</param>
/// <param name="Status">The status text, rendered by the module that reports the activity.</param>
/// <param name="Value">The progress percentage between 0 and 100.</param>
/// <remarks>
///     The projecting module renders its own wording rather than handing the ViewModel a typed activity to render: a
///     Processing Run and a Plugin List refresh share no common vocabulary worth inventing, and the Plugin List
///     Presentation Adapter's activity-to-wording mapping is behaviour it legitimately owns.
/// </remarks>
internal readonly record struct ActivityProjection(bool IsActive, string Status, double Value)
{
    /// <summary>
    ///     Gets the report of an activity that is not running, which contributes nothing to the progress channel.
    /// </summary>
    public static ActivityProjection None { get; } = new(false, string.Empty, 0);
}
