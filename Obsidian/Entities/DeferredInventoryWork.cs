using Microsoft.Extensions.Logging;
using Obsidian.API.Data;
using System.Threading;

namespace Obsidian.Entities;

/// <summary>
/// Work that takes a player's inventory lock, asked for while the current flow runs plugins' code holding a player's
/// inventory lock, and run as soon as it lets go: like the armour a hurt wears, or a death's drops, when a plugin's
/// item hurts its holder from its inventory tick. The lock isn't re-entrant, so waiting for it there would never end.
/// Travel also uses sections to queue reentrant operations through its complete packet and callback sequence.
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
    private readonly IClient? packetClient;
    private readonly IClient? sequenceClient;

    public DeferredInventoryWork(IClient? packetClient = null, IClient? sequenceClient = null)
    {
        this.packetClient = packetClient;
        this.sequenceClient = sequenceClient;
    }

    // Set as the queue runs, after which work runs at once.
    private bool closed;

    /// <summary>
    /// Runs <paramref name="work"/>, which takes a player's inventory lock, now; or, inside a section (see
    /// <see cref="Enter"/>), once its flow let go of the lock, after the work queued before it.
    /// </summary>
    /// <returns>The work's task, or a completed one when it was queued.</returns>
    /// <param name="sequenceClient">
    /// For travel, the player's client: another player's travel section must not defer a passenger's transfer.
    /// Omit for inventory work, which must wait for every enclosing section.
    /// </param>
    public static ValueTask RunOrDeferAsync(Func<ValueTask> work, IClient? sequenceClient = null)
    {
        for (var section = current.Value; section is not null; section = section.Previous)
        {
            // Inventory sections protect every player. Travel sections protect their own player, so a vehicle
            // can still await its player passengers' actual transfers before seating them again.
            if ((section.Work.sequenceClient is null || sequenceClient is null
                || ReferenceEquals(section.Work.sequenceClient, sequenceClient)) && section.Work.TryQueue(work))
                return ValueTask.CompletedTask;
        }

        return work();
    }

    // Defer the entire death request before capturing its transaction, rather than reserve a death that the
    // current travel would then wait for. Inventory-only sections retain their existing death snapshot rules.
    internal static bool TryDeferSequence(IClient client, Func<ValueTask> work)
    {
        for (var section = current.Value; section is not null; section = section.Previous)
        {
            if (ReferenceEquals(section.Work.sequenceClient, client) && section.Work.TryQueue(work))
                return true;
        }

        return false;
    }

    // Synchronous side effects that need an earlier lock in the order also wait for the transaction to finish.
    // Only inventory transactions hold click locks; travel sections hold none, so they never delay this work
    // (a vehicle seats its carried passengers again within its own travel).
    internal static bool TryDefer(Action work)
    {
        for (var section = current.Value; section is not null; section = section.Previous)
        {
            if (section.Work.sequenceClient is not null)
                continue;

            var queued = section.Work.TryQueue(() =>
            {
                work();
                return ValueTask.CompletedTask;
            });
            if (queued)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Enters around a whole click, hook or respawn; dispose after releasing its locks, even on failure.
    /// Set <paramref name="packetClient"/> when its packets must wait for that release too.
    /// Other clients send normally.
    /// </summary>
    internal static IAsyncDisposable Begin(
        ILogger logger, Action? completed = null, IClient? packetClient = null) =>
        new Transaction(logger, completed, packetClient);

    /// <summary>
    /// Queues a packet and its plugin handlers until travel releases chunkUpdates.
    /// Queueing the entire send preserves cancellation and packet order when the transaction drains.
    /// </summary>
    internal static bool TryDeferPacket(IClient client, IClientboundPacket packet) =>
        current.Value is Section section && ReferenceEquals(section.Work.packetClient, client)
            && section.Work.TryQueue(() => client.QueuePacketAsync(packet));

    // Raw sends join the same FIFO as handled packets, without running their handlers a second time.
    internal static bool TryDeferSend(Client client, IClientboundPacket packet) =>
        current.Value is Section section && ReferenceEquals(section.Work.packetClient, client)
            && section.Work.TryQueue(() =>
            {
                client.QueueWithoutHandlers(packet);
                return ValueTask.CompletedTask;
            });

    private sealed class Transaction : IAsyncDisposable
    {
        private readonly ILogger logger;
        private readonly Action? completed;
        private readonly DeferredInventoryWork work;
        private readonly IDisposable section;
        private readonly DeferredAttachmentChanges notifications = new();

        internal Transaction(
            ILogger logger, Action? completed, IClient? packetClient)
        {
            this.logger = logger;
            this.completed = completed;
            this.work = new DeferredInventoryWork(packetClient);
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

    private sealed class Section(DeferredInventoryWork work, Section? previous) : IDisposable
    {
        internal DeferredInventoryWork Work { get; } = work;
        internal Section? Previous { get; } = previous;

        public void Dispose() => current.Value = this.Previous;
    }
}
