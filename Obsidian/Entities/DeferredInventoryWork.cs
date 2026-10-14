using Microsoft.Extensions.Logging;
using Obsidian.API.Data;
using System.Threading;

namespace Obsidian.Entities;

/// <summary>
/// Work that takes a player's inventory lock, asked for while the current flow runs plugins' code holding a player's
/// inventory lock, and run as soon as it lets go: like the armour a hurt wears, or a death's drops, when a plugin's
/// item hurts its holder from its inventory tick. The lock isn't re-entrant, so waiting for it there would never end.
/// Respawns also defer their packets and plugin callbacks until the player's chunk-update lock is released.
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
    private static readonly AsyncLocal<DeferredInventoryWork?> current = new();

    private readonly Queue<Func<ValueTask>> queue = new();
    private readonly bool deferPackets;

    public DeferredInventoryWork(bool deferPackets = false) => this.deferPackets = deferPackets;

    // Set as the queue runs, after which work runs at once.
    private bool closed;

    /// <summary>
    /// Runs <paramref name="work"/>, which takes a player's inventory lock, now; or, inside a section (see
    /// <see cref="Enter"/>), once its flow let go of the lock, after the work queued before it.
    /// </summary>
    /// <returns>The work's task, or a completed one when it was queued.</returns>
    public static ValueTask RunOrDeferAsync(Func<ValueTask> work) =>
        current.Value is DeferredInventoryWork section && section.TryQueue(work)
            ? ValueTask.CompletedTask
            : work();

    /// <summary>
    /// Enters around a whole click, hook or respawn; dispose after releasing its locks, even on failure.
    /// Set <paramref name="deferPackets"/> when packet callbacks must wait for that release too.
    /// </summary>
    internal static IAsyncDisposable Begin(ILogger logger, Action? completed = null, bool deferPackets = false) =>
        new Transaction(logger, completed, deferPackets);

    /// <summary>
    /// Queues a packet and its plugin handlers until a respawn or dimension transfer releases chunkUpdates.
    /// Queueing the entire send preserves cancellation and packet order when the transaction drains.
    /// </summary>
    internal static bool TryDeferPacket(IClient client, IClientboundPacket packet) =>
        current.Value is { deferPackets: true } section
            && section.TryQueue(() => client.QueuePacketAsync(packet));

    private sealed class Transaction : IAsyncDisposable
    {
        private readonly ILogger logger;
        private readonly Action? completed;
        private readonly DeferredInventoryWork work;
        private readonly IDisposable section;
        private readonly DeferredAttachmentChanges notifications = new();

        internal Transaction(ILogger logger, Action? completed, bool deferPackets)
        {
            this.logger = logger;
            this.completed = completed;
            this.work = new DeferredInventoryWork(deferPackets);
            this.section = this.work.Enter();
        }

        public ValueTask DisposeAsync()
        {
            // Notifications see the completed transfer, without click locks. Deaths they request join the queue.
            this.notifications.Dispose();
            this.completed?.Invoke();
            this.section.Dispose();
            return this.work.deferPackets ? this.DrainPacketsAsync() : this.work.RunAsync(this.logger);
        }

        private async ValueTask DrainPacketsAsync()
        {
            // A packet handler may kill or respawn its player. Finish this packet sequence first, so its saved
            // health and position cannot arrive after the next death's packets. All locks are already released.
            await using var callbacks = Begin(this.logger);
            await this.work.RunAsync(this.logger);
        }
    }

    /// <summary>
    /// Starts the section, in the current flow, until the returned handle is disposed. Call it holding the lock,
    /// right around the plugins' code.
    /// </summary>
    public IDisposable Enter()
    {
        // Not async, so that the value stays set in the caller's flow.
        var previous = current.Value;
        current.Value = this;

        return new Section(previous);
    }

    /// <summary>
    /// Runs the queued work in order, once the lock was let go. Work that throws is logged with
    /// <paramref name="logger"/>, as it has no caller left to throw to, and the rest still runs.
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
                await work();
            }
            catch (Exception exception)
            {
                WorkFailed(logger, exception);
            }
        }
    }

    private bool TryQueue(Func<ValueTask> work)
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

    private sealed class Section(DeferredInventoryWork? previous) : IDisposable
    {
        public void Dispose() => current.Value = previous;
    }
}
