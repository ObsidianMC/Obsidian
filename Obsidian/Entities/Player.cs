using Microsoft.Extensions.Logging;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Play.Clientbound;
using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Obsidian.Entities;

/// <summary>
/// A player connected through a Minecraft client. Their gameplay is <see cref="PlayerBase"/>'s; this adds the
/// connection: the packets they're sent, the chunks and entities their client is shown, and leaving.
/// </summary>
public sealed partial class Player : PlayerBase, IClientPlayer
{
    public IClient Client { get; internal set; }

    public override ILogger Logger => this.Client.Logger;

    public override bool SendPacket(IClientboundPacket packet) => this.Client.SendPacket(packet);

    public override ValueTask QueuePacketAsync(IClientboundPacket packet) => this.Client.QueuePacketAsync(packet);

    internal override IClient? Connection => this.Client;

    internal override bool Connected => this.Client.Connected;

    public override int Ping => this.Client.Ping;

    public override string? ClientIP => this.Client.Ip;

    // The other players the client has been shown.
    internal HashSet<IPlayer> visiblePlayers = [];

    // Unsent chunks, the current nearest-first loading pass, and the client's view. Guarded by chunkUpdates so packet
    // handlers and the level tick can't update the client's chunks at the same time.
    private readonly HashSet<long> pendingChunks = [];
    private readonly Queue<long> pendingChunkLoads = [];
    private const int MaximumChunkLoadsPerPass = 16;
    private static readonly TimeSpan ChunkLoadTimeBudget = TimeSpan.FromMilliseconds(5);

    // Set once the player left for reconfiguration, after which the client's next player sends its chunks.
    private bool chunksStopped;

    // Vanilla's chunk batch flow control (PlayerChunkSender): chunks go out in batches the client acknowledges with how
    // many chunks a tick it can take, so a slow connection isn't flooded and later packets (keep-alives) don't wait
    // behind megabytes of chunks. The client's rate tops up a quota every tick. One batch may be unacknowledged at first,
    // ten once the client has answered. Guarded by chunkBatchLock, since acknowledgements arrive on the network thread.
    private readonly Lock chunkBatchLock = new();
    private float desiredChunksPerTick = 9;
    private float chunkBatchQuota;
    private int unacknowledgedChunkBatches;
    private int maxUnacknowledgedChunkBatches = 1;
    private (int X, int Z)? chunkCacheCenter;
    private int chunkViewDistance;

    [SetsRequiredMembers]
    internal Player(Guid uuid, string username, IClient client, IWorld world) : base(uuid, username, client.Id, world)
    {
        this.Client = client;
    }

    // The client's pipeline, which runs the window snapshots' checks in turn with its other packets (see
    // Client.QueueInTurn). A connection that isn't a server's (a test's, say) has no pipeline and runs no handlers.
    private protected override ValueTask<bool> RunQueueHandlersAsync(IClientboundPacket packet) =>
        this.Client is Client client
            ? client.RunQueueHandlersAsync(packet)
            : ValueTask.FromResult(true);

    private protected override void SendInTurn(IClientboundPacket packet)
    {
        if (this.Client is Client client)
            client.SendInTurn(packet);
        else
            this.Client.SendPacket(packet);
    }

    private protected override ValueTask RunInTurnAsync(Func<ValueTask> work)
    {
        if (this.Client is not Client client)
            return work();

        client.QueueInTurn(work);
        return ValueTask.CompletedTask;
    }

    public override async ValueTask KickAsync(string reason) => await this.Client.DisconnectAsync(reason);
    public override async ValueTask KickAsync(ChatMessage reason) => await this.Client.DisconnectAsync(reason);
    public override async ValueTask DisconnectAsync(ChatMessage reason) => await this.Client.DisconnectAsync(reason);

    private protected override void ResetClientView()
    {
        this.ResetAttachmentSync();
        this.visiblePlayers.Clear();
    }

    internal ValueTask UnloadChunkAsync(int x, int z) => LoadedChunks.Contains(NumericsHelper.IntsToLong(x, z))
        ? this.QueuePacketAsync(new ForgetLevelChunkPacket(x, z))
        : default;

    private protected override async ValueTask TrySpawnPlayerAsync(VectorD position)
    {
        var entityBroadcastDistance = this.Server.Configuration.EntityBroadcastRangePercentage;

        foreach (var player in Level.GetPlayersInRange(position, entityBroadcastDistance))
        {
            if (player.EntityId == this.EntityId)
                continue;

            if (player.Alive && !visiblePlayers.Contains(player))
            {
                visiblePlayers.Add(player);

                player.SpawnEntity();
                this.ResetVisiblePlayerAttachments(player.EntityId);
            }
        }

        if (visiblePlayers.Count == 0)
            return;

        var removed = ArrayPool<int>.Shared.Rent(visiblePlayers.Count);

        var index = 0;
        visiblePlayers.RemoveWhere(visiblePlayer =>
        {
            // A player who left or changed level isn't in this level anymore, wherever they were last.
            if (!visiblePlayer.IsInRange(this, entityBroadcastDistance) || !Level.Players.ContainsKey(visiblePlayer.Uuid))
            {
                removed[index++] = visiblePlayer.EntityId;
                return true;
            }
            return false;
        });

        if (index > 0)
            await this.QueuePacketAsync(new RemoveEntitiesPacket(removed.AsSpan(0, index).ToArray()));

        ArrayPool<int>.Shared.Return(removed);
    }

    /// <summary>
    /// Updates the chunks the client has to its view around the player, like vanilla's chunk tracking: the client learns
    /// the new center, chunks out of view are forgotten, and chunks in view are sent nearest first. Chunks that aren't
    /// generated yet are queued for generation and sent once they are (see <see cref="SendPendingChunksAsync"/>). Called
    /// with chunkUpdates held.
    /// </summary>
    private protected override async Task<bool> UpdateChunksUnderLockAsync(bool unloadAll, int distance = 0)
    {
        if (unloadAll)
        {
            var tracked = TrackedEntities.Keys.ToArray();
            TrackedEntities.Clear();
            if (!Respawning && tracked.Length > 0)
                await this.Client.QueuePacketAsync(new RemoveEntitiesPacket(tracked));
            if (!Respawning)
            {
                foreach (var value in LoadedChunks)
                {
                    NumericsHelper.LongToInts(value, out var x, out var z);
                    await UnloadChunkAsync(x, z);
                }
            }

            LoadedChunks.Clear();
            this.pendingChunks.Clear();
            this.pendingChunkLoads.Clear();
            this.chunkCacheCenter = null;
        }

        var (centerX, centerZ) = Position.ToChunkCoord();

        // The client drops chunks outside the range around its center, so the center goes first.
        if (this.chunkCacheCenter != (centerX, centerZ))
        {
            this.chunkCacheCenter = (centerX, centerZ);
            this.pendingChunkLoads.Clear();
            await this.Client.QueuePacketAsync(new SetChunkCacheCenterPacket(centerX, centerZ));
        }

        // Like vanilla, at least 2.
        var viewDistance = Math.Max(2, distance < 1 ? ClientInformation.ViewDistance : distance);
        if (this.chunkViewDistance != viewDistance)
            this.pendingChunkLoads.Clear();
        this.chunkViewDistance = viewDistance;

        foreach (var value in LoadedChunks)
        {
            NumericsHelper.LongToInts(value, out var x, out var z);
            if (!this.IsInView(x, z) && LoadedChunks.TryRemove(value))
                await this.Client.QueuePacketAsync(new ForgetLevelChunkPacket(x, z));
        }

        this.pendingChunks.RemoveWhere(value =>
        {
            NumericsHelper.LongToInts(value, out var x, out var z);
            return !this.IsInView(x, z);
        });

        for (var x = centerX - this.chunkViewDistance - 1; x <= centerX + this.chunkViewDistance + 1; x++)
        {
            for (var z = centerZ - this.chunkViewDistance - 1; z <= centerZ + this.chunkViewDistance + 1; z++)
            {
                var value = NumericsHelper.IntsToLong(x, z);
                if (this.IsInView(x, z) && !LoadedChunks.Contains(value))
                    this.pendingChunks.Add(value);
            }
        }

        return await this.SendReadyChunksAsync();
    }

    /// <summary>
    /// Waits for a chunk send under way and stops sending this player chunks: they left to go back through
    /// configuration, and the client's next player gets its chunks.
    /// </summary>
    internal async Task StopSendingChunksAsync()
    {
        await this.chunkUpdates.WaitAsync();
        this.chunksStopped = true;
        this.chunkUpdates.Release();
    }

    /// <summary>
    /// Sends the chunks in view that weren't generated when they were last asked for and are now. The server's chunk
    /// loop calls it every tick, independently of the world tick; it skips the tick while the chunks are being updated.
    /// </summary>
    internal async Task SendPendingChunksAsync()
    {
        lock (this.chunkBatchLock)
        {
            if (this.unacknowledgedChunkBatches < this.maxUnacknowledgedChunkBatches)
            {
                var cap = Math.Max(1, this.desiredChunksPerTick);
                this.chunkBatchQuota = Math.Min(this.chunkBatchQuota + this.desiredChunksPerTick, cap);
            }
        }

        if (this.pendingChunks.Count == 0 || !await this.chunkUpdates.WaitAsync(0))
            return;

        try
        {
            await this.SendReadyChunksAsync();
        }
        catch (OperationCanceledException)
        {
            // The client disconnected while chunks were queued for it, which shouldn't reach the level's tick: the server
            // loop takes a cancellation for its own shutdown.
        }
        finally
        {
            this.chunkUpdates.Release();
        }
    }

    // Bound disk reads and packet queuing so login and movement don't wait for the entire view. Finish a pass before
    // retrying ungenerated chunks, so they can't prevent the rest of the view from being requested. Called under chunkUpdates.
    private async Task<bool> SendReadyChunksAsync()
    {
        if (this.chunksStopped)
            return true;

        if (this.pendingChunks.Count == 0 || this.chunkCacheCenter is not var (centerX, centerZ))
            return this.pendingChunks.Count == 0;

        if (this.pendingChunkLoads.Count == 0)
        {
            var chunks = this.pendingChunks.ToArray();
            var distances = new int[chunks.Length];
            for (var i = 0; i < chunks.Length; i++)
            {
                NumericsHelper.LongToInts(chunks[i], out var x, out var z);
                distances[i] = (x - centerX) * (x - centerX) + (z - centerZ) * (z - centerZ);
            }

            Array.Sort(distances, chunks);
            foreach (var value in chunks)
                this.pendingChunkLoads.Enqueue(value);
        }

        int allowed;
        lock (this.chunkBatchLock)
        {
            allowed = this.unacknowledgedChunkBatches < this.maxUnacknowledgedChunkBatches
                ? (int)Math.Floor(this.chunkBatchQuota)
                : 0;
        }

        if (allowed <= 0)
            return false;

        var batch = new List<IChunk>();
        var started = Stopwatch.GetTimestamp();
        for (var count = 0;
             count < MaximumChunkLoadsPerPass && batch.Count < allowed && this.pendingChunkLoads.TryDequeue(out var value);
             count++)
        {
            if (this.pendingChunks.Contains(value))
            {
                NumericsHelper.LongToInts(value, out var x, out var z);
                if (await Level.GetChunkAsync(x, z) is IChunk { IsGenerated: true } chunk)
                {
                    batch.Add(chunk);
                    LoadedChunks.Add(value);
                    this.pendingChunks.Remove(value);
                }
            }

            if (Stopwatch.GetElapsedTime(started) >= ChunkLoadTimeBudget)
                break;
        }

        if (batch.Count > 0)
        {
            lock (this.chunkBatchLock)
            {
                this.unacknowledgedChunkBatches++;
                this.chunkBatchQuota -= batch.Count;
            }

            await this.Client.QueuePacketAsync(new ChunkBatchStartPacket());
            foreach (var chunk in batch)
                await this.Client.QueuePacketAsync(new LevelChunkWithLightPacket(chunk));

            await this.Client.QueuePacketAsync(new ChunkBatchFinishedPacket { BatchSize = batch.Count });
        }

        return this.pendingChunks.Count == 0;
    }

    /// <summary>
    /// Takes the client's acknowledgement of a chunk batch and the chunks a tick it asks for (vanilla's
    /// <c>onChunkBatchReceivedByClient</c>).
    /// </summary>
    internal void OnChunkBatchReceived(float chunksPerTick)
    {
        lock (this.chunkBatchLock)
        {
            this.unacknowledgedChunkBatches = Math.Max(0, this.unacknowledgedChunkBatches - 1);
            this.desiredChunksPerTick = float.IsNaN(chunksPerTick) ? .01f : Math.Clamp(chunksPerTick, .01f, 64);
            if (this.unacknowledgedChunkBatches == 0)
                this.chunkBatchQuota = 1;

            this.maxUnacknowledgedChunkBatches = 10;
        }
    }

    // Vanilla's ChunkTrackingView.contains, which counts the neighbors the client needs to render the edge: a cylinder
    // around the center that reaches one chunk past the view distance along the axes.
    private bool IsInView(int chunkX, int chunkZ)
    {
        if (this.chunkCacheCenter is not var (centerX, centerZ))
            return false;

        long dx = Math.Max(0, Math.Abs(chunkX - centerX) - 2);
        long dz = Math.Max(0, Math.Abs(chunkZ - centerZ) - 2);
        return dx * dx + dz * dz < (long)this.chunkViewDistance * this.chunkViewDistance;
    }
}
