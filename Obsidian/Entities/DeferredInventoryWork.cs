using Microsoft.Extensions.Logging;
using Obsidian.API.Data;
using System.Threading;

namespace Obsidian.Entities;

/// <summary>
/// Work that takes a player's inventory lock, asked for while the current flow runs plugins' code holding a player's
/// inventory lock, and run as soon as it lets go: like the armour a hurt wears, or a death's drops, when a plugin's
/// item hurts its holder from its inventory tick. The lock isn't re-entrant, so waiting for it there would never end.
/// Respawns and level changes use sections too, around the plugins' code they run holding the player's chunk lock.
/// </summary>
/// <remarks>
/// <para>
/// Code that runs plugins' code holding the lock enters a section around it, and runs what was queued once it let
/// go of the lock, even when the plugin's code threw:
/// </para>
/// <code>
/// var deferred = new DeferredInventoryWork();
/// try
/// {
///     using (await inventory.LockAsync())
///     using (deferred.Enter())
///         await RunPluginCodeAsync();
/// }
/// finally
/// {
///     await deferred.RunAsync(logger);
/// }
/// </code>
/// <para>
/// Work that takes the lock goes through <see cref="RunOrDeferAsync"/>. Inside a section it's queued, whichever
/// player's inventory it locks, so that two players' hooks hurting each other can't wait for each other's locks.
/// The section follows the flow, and tasks the plugin's code starts inherit it: they queue their work too, so that
/// nothing enters the lock beside the flow holding it, and work asked for once the queue ran runs at once.
/// </para>
/// </remarks>
internal sealed partial class DeferredInventoryWork
{
    // The section the current flow is in, if any.
    private static readonly AsyncLocal<Section?> current = new();

    private readonly Queue<Func<ValueTask>> queue = new();

    // Set as the queue runs, after which work runs at once.
    private bool closed;

    /// <summary>
    /// Runs <paramref name="work"/>, which takes a player's inventory lock, now; or, inside a section (see
    /// <see cref="Enter"/>), once its flow let go of the lock, after the work queued before it. A section whose queue
    /// already ran passes the work to the section around it, if that one still holds its lock.
    /// </summary>
    /// <returns>The work's task, or a completed one when it was queued.</returns>
    public static ValueTask RunOrDeferAsync(Func<ValueTask> work) =>
        TryQueue(work) ? ValueTask.CompletedTask : work();

    // Synchronous side effects that need an earlier lock in the order (riding changes, menus' Closed callbacks) also
    // wait for the transaction to finish.
    internal static bool TryDefer(Action work) => TryQueue(() =>
    {
        work();
        return ValueTask.CompletedTask;
    });

    /// <summary>
    /// Enters around a whole click, hook or respawn; dispose after releasing its locks, even on failure.
    /// </summary>
    internal static IAsyncDisposable Begin(ILogger logger, Action? completed = null) =>
        new Transaction(logger, completed);

    // Queues the work in the innermost section still taking work, if any.
    private static bool TryQueue(Func<ValueTask> work)
    {
        for (var section = current.Value; section is not null; section = section.Previous)
        {
            if (section.Work.TryEnqueue(work))
                return true;
        }

        return false;
    }

    private sealed class Transaction : IAsyncDisposable
    {
        private readonly ILogger logger;
        private readonly Action? completed;
        private readonly DeferredInventoryWork work = new();
        private readonly IDisposable section;
        private readonly DeferredAttachmentChanges notifications = new();

        internal Transaction(ILogger logger, Action? completed)
        {
            this.logger = logger;
            this.completed = completed;
            this.section = this.work.Enter();
        }

        public ValueTask DisposeAsync()
        {
            // Notifications see the completed transfer, without click locks. Deaths they request join the queue.
            try
            {
                this.notifications.Dispose();
                this.completed?.Invoke();
            }
            finally
            {
                this.section.Dispose();
            }

            return this.work.RunAsync(this.logger);
        }
    }

    /// <summary>
    /// Starts the section, in the current flow, until the returned handle is disposed. Call it holding the lock,
    /// right around the plugins' code.
    /// </summary>
    public IDisposable Enter()
    {
        // Not async, so that the value stays set in the caller's flow.
        var section = new Section(this, current.Value);
        current.Value = section;

        return section;
    }

    /// <summary>
    /// Closes the section and hands its queued work to the nearest open parent, or runs it in order if every
    /// enclosing lock was let go. Work that throws is logged with <paramref name="logger"/>, as it has no caller
    /// left to throw to, and the rest still runs.
    /// </summary>
    public async ValueTask RunAsync(ILogger logger)
    {
        Func<ValueTask>[] queued;

        lock (this.queue)
        {
            this.closed = true;
            queued = [.. this.queue];
            this.queue.Clear();
        }

        foreach (var work in queued)
        {
            try
            {
                await RunOrDeferAsync(work);
            }
            catch (Exception exception)
            {
                WorkFailed(logger, exception);
            }
        }
    }

    private bool TryEnqueue(Func<ValueTask> work)
    {
        lock (this.queue)
        {
            if (this.closed)
                return false;

            this.queue.Enqueue(work);
            return true;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Inventory work deferred past a plugin's code failed")]
    private static partial void WorkFailed(ILogger logger, Exception exception);

    private sealed class Section(DeferredInventoryWork work, Section? previous) : IDisposable
    {
        internal DeferredInventoryWork Work { get; } = work;
        internal Section? Previous { get; } = previous;

        public void Dispose() => current.Value = this.Previous;
    }
}
