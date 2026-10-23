using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Obsidian.Entities;

/// <summary>
/// A server-side player: one without a Minecraft network connection, which plugins add through
/// <see cref="IServer.AddServerPlayerAsync"/>. It plays like any other player (see <see cref="PlayerBase"/>), but what
/// it would be shown is dropped, and nothing about it is saved. Like a client's player, it keeps the chunks around it
/// loaded (see <see cref="LoadChunksAsync"/>).
/// </summary>
public sealed class ServerPlayer : PlayerBase
{
    // Like a client's player: a pass loads at most this many chunks and stops once it's taken this long, so that
    // moving far doesn't hold up the chunk loop; the next passes load the rest.
    private const int MaximumChunkLoadsPerPass = 16;
    private static readonly TimeSpan ChunkLoadTimeBudget = TimeSpan.FromMilliseconds(5);

    // Joining, joined, or removed. A leave can finish while a join handler is awaiting; that join must roll back.
    private int lifecycle;

    // The chunk the loaded square is around, and whether some of it isn't loaded yet. Guarded by chunkUpdates.
    private (int X, int Z)? chunkCenter;
    private bool chunksPending;

    internal bool CompleteJoin() => Interlocked.CompareExchange(ref this.lifecycle, 1, 0) == 0;

    internal void CancelJoin() => Interlocked.Exchange(ref this.lifecycle, 2);

    [SetsRequiredMembers]
    public ServerPlayer(Guid uuid, string username, IServer server, IWorld world)
        : base(uuid, username, Obsidian.Server.GetNextEntityId(), world)
    {
        this.Server = server;
        this.ClientInformation = this.ClientInformation with { AllowsListing = true };
    }

    public override ILogger Logger => (this.Server as Server)?.logger ?? NullLogger.Instance;

    // There is no client to receive packets, so anything the server would show this player is dropped.
    public override bool SendPacket(IClientboundPacket packet) => false;

    public override ValueTask QueuePacketAsync(IClientboundPacket packet) => default;

    internal override bool Connected => Volatile.Read(ref this.lifecycle) != 2;

    public override int Ping => 0;

    public override string? ClientIP => null;

    public override ValueTask DisconnectAsync(ChatMessage reason) => new(this.Server.RemoveServerPlayerAsync(this));

    public override ValueTask KickAsync(ChatMessage reason) => new(this.Server.RemoveServerPlayerAsync(this));

    public override ValueTask KickAsync(string reason) => new(this.Server.RemoveServerPlayerAsync(this));

    public override ValueTask SendMessageAsync(ChatMessage message, Guid sender, SecureMessageSignature messageSignature) =>
        default;

    public override ValueTask SpawnParticleAsync(ParticleData data) => default;

    // Server-side players aren't saved: they start at the spawn, and their permissions last until they're removed.
    public override Task SaveAsync() => Task.CompletedTask;

    public override Task LoadAsync(bool loadFromPersistentWorld = true)
    {
        if (!double.IsFinite(this.Position.X) || !double.IsFinite(this.Position.Y) || !double.IsFinite(this.Position.Z) || this.Position == default)
            this.Position = this.Level.LevelData.SpawnPosition;

        if (!this.Alive)
            this.Health = 20f;

        return Task.CompletedTask;
    }

    public override Task LoadPermsAsync() => Task.CompletedTask;

    /// <summary>
    /// Loads the chunks around the player once they've moved to another chunk, or some weren't generated yet. The
    /// server's chunk loop calls it every tick, independently of the world tick; it skips the tick while the chunks
    /// are being updated.
    /// </summary>
    internal async Task LoadChunksAsync()
    {
        if (!this.chunksPending && this.chunkCenter == this.Position.ToChunkCoord() || !await this.chunkUpdates.WaitAsync(0))
            return;

        try
        {
            await this.UpdateChunksUnderLockAsync(unloadAll: false);
        }
        finally
        {
            this.chunkUpdates.Release();
        }
    }

    // The player keeps loaded a square around their chunk: the simulation distance, in which mobs and blocks tick around
    // a player, and a border, because a chunk ticks its blocks only with its neighbours loaded. A client's player keeps
    // what it was sent, out to its view distance, but nobody sees these chunks. LoadedChunks holds them, which keeps the
    // level from unloading them.
    private protected override async Task<bool> UpdateChunksUnderLockAsync(bool unloadAll, int distance = 0)
    {
        if (unloadAll)
            this.LoadedChunks.Clear();

        var (centerX, centerZ) = this.Position.ToChunkCoord();
        var radius = (distance < 1 ? this.Server.Configuration.SimulationDistance : distance) + 1;
        this.chunkCenter = (centerX, centerZ);

        foreach (var value in this.LoadedChunks)
        {
            NumericsHelper.LongToInts(value, out var x, out var z);
            if (Math.Abs(x - centerX) > radius || Math.Abs(z - centerZ) > radius)
                this.LoadedChunks.TryRemove(value);
        }

        // Nearest first, so that the player's own chunk loads before the edge.
        var missing = new List<(int X, int Z)>();
        for (var x = centerX - radius; x <= centerX + radius; x++)
        {
            for (var z = centerZ - radius; z <= centerZ + radius; z++)
            {
                if (!this.LoadedChunks.Contains(NumericsHelper.IntsToLong(x, z)))
                    missing.Add((x, z));
            }
        }

        missing.Sort((a, b) => Distance(a).CompareTo(Distance(b)));
        int Distance((int X, int Z) chunk) =>
            (chunk.X - centerX) * (chunk.X - centerX) + (chunk.Z - centerZ) * (chunk.Z - centerZ);

        var started = Stopwatch.GetTimestamp();
        var loads = 0;
        foreach (var (x, z) in missing)
        {
            if (loads++ >= MaximumChunkLoadsPerPass || Stopwatch.GetElapsedTime(started) >= ChunkLoadTimeBudget)
                break;

            // Asking for an ungenerated chunk queues its generation; a later pass picks it up.
            if (await this.Level.GetChunkAsync(x, z) is IChunk { IsGenerated: true })
                this.LoadedChunks.Add(NumericsHelper.IntsToLong(x, z));
        }

        this.chunksPending = this.LoadedChunks.Count < (radius * 2 + 1) * (radius * 2 + 1);
        return !this.chunksPending;
    }

    public override Task SavePermsAsync() => Task.CompletedTask;
}
