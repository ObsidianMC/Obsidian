using System.Threading;

namespace Obsidian.API.BlockEntities;

public abstract partial class BlockEntity
{
    private readonly Lock liveCallLock = new();
    private bool removed;
    private bool removalRequested;
    private TaskCompletionSource? liveCall;

    // OnItemsChanged is synchronous. Only its own thread may finish a self-removal without waiting for itself.
    [ThreadStatic]
    private static BlockEntity? callingLiveEntity;

    /// <summary>
    /// Reserves a live callback against removal, inside its plugin call guard. The reservation holds no lock over
    /// the callback; removal stops new calls and publishes IsRemoved once this call finishes.
    /// </summary>
    internal void RunIfLive(Action code)
    {
        TaskCompletionSource completion;
        lock (this.liveCallLock)
        {
            if (this.removalRequested || this.removed || this.liveCall is not null)
                return;

            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            this.liveCall = completion;
        }

        var previous = callingLiveEntity;
        callingLiveEntity = this;
        try
        {
            code();
        }
        finally
        {
            callingLiveEntity = previous;
            lock (this.liveCallLock)
            {
                Volatile.Write(ref this.removed, this.removalRequested);
                this.liveCall = null;
            }

            completion.SetResult();
        }
    }

    /// <summary>
    /// Waits outside engine locks before completing removal or chunk unload. Lifecycle callbacks still run on the
    /// level's existing serial callback queue; a live callback removing itself must not wait on its own return.
    /// </summary>
    internal Task WaitForLiveCallAsync()
    {
        if (ReferenceEquals(callingLiveEntity, this))
            return Task.CompletedTask;

        lock (this.liveCallLock)
            return this.liveCall?.Task ?? Task.CompletedTask;
    }
}
