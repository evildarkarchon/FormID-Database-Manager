using System;

namespace FormID_Database_Manager.TestUtilities.Mocks;

/// <summary>
/// A synchronous IProgress implementation that invokes callbacks immediately on the calling thread,
/// avoiding the async callback behavior of <see cref="Progress{T}"/> that can cause race conditions
/// and timing issues in tests.
/// </summary>
/// <remarks>
/// The standard <see cref="Progress{T}"/> class captures the <see cref="System.Threading.SynchronizationContext"/>
/// when it's created and posts callbacks to that context. In test environments without a proper
/// synchronization context, this can cause callbacks to be lost or delayed, leading to flaky tests.
/// </remarks>
/// <typeparam name="T">The type of progress value.</typeparam>
public class SynchronousProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;

    /// <summary>
    /// Initializes a new instance of <see cref="SynchronousProgress{T}"/>.
    /// </summary>
    /// <param name="handler">The action to invoke when progress is reported.</param>
    public SynchronousProgress(Action<T> handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    /// <inheritdoc />
    public void Report(T value)
    {
        _handler(value);
    }
}
