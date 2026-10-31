using System.Runtime.ExceptionServices;

namespace FormID_Database_Manager.Services;

/// <summary>
///     The one unwrap that keeps cancellation run control flow when it reaches a Plugin read wrapped in another
///     exception, so no catch can mistake it for a Failed Plugin or a Processing Warning.
/// </summary>
/// <remarks>
///     Mutagen enriches some failures by wrapping them, a record-reading <c>RecordException</c> among them, and a
///     getter invoked through a dynamic proxy surfaces its exception inside a <c>TargetInvocationException</c>. Either
///     way the cancellation is not necessarily the caught type, so every catch that classifies a Plugin read failure
///     calls this first.
/// </remarks>
internal static class NestedCancellation
{
    /// <summary>
    ///     Rethrows the first <see cref="OperationCanceledException" /> in an exception's inner-exception chain,
    ///     starting with the exception itself, and returns normally when the chain contains none.
    /// </summary>
    /// <param name="exception">The possible wrapper caught while reading a Plugin.</param>
    /// <exception cref="OperationCanceledException">
    ///     The chain contains cancellation. The original instance is rethrown with its original stack trace.
    /// </exception>
    internal static void RethrowIfPresent(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is OperationCanceledException cancellation)
            {
                ExceptionDispatchInfo.Capture(cancellation).Throw();
            }
        }
    }
}
