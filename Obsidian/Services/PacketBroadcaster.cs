using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Obsidian.API.World;
using Obsidian.Entities;
using Obsidian.Hosting;
using Obsidian.Net;
using Obsidian.WorldData;
using System.Threading;

namespace Obsidian.Services;

public sealed class PacketBroadcaster(IServer server, ILogger<PacketBroadcaster> logger, IServerEnvironment environment) : BackgroundService, IPacketBroadcaster
{
    private readonly IServer server = server;
    private readonly IServerEnvironment environment = environment;
    private readonly PriorityQueue<QueuedPacket, (int Priority, long Order)> priorityQueue = new();
    private readonly Lock queueLock = new();
    private long nextOrder;

    private void Enqueue(QueuedPacket packet, int priority)
    {
        lock (queueLock)
            priorityQueue.Enqueue(packet, (priority, nextOrder++));
    }

    private QueuedPacket[] TakeBatch()
    {
        lock (queueLock)
        {
            var batch = new QueuedPacket[priorityQueue.Count];
            for (var index = 0; index < batch.Length; index++)
                batch[index] = priorityQueue.Dequeue();
            return batch;
        }
    }

    public void QueuePacketTo(IClientboundPacket packet, params int[] ids)
    {
        this.Enqueue(new() { Packet = packet, IncludedIds = ids }, 1);
    }

    public void QueuePacketTo(IClientboundPacket packet, int priority, params int[] ids)
    {
        this.Enqueue(new()
        {
            Packet = packet,
            IncludedIds = ids
        }, priority);
    }

    public void QueuePacket(IClientboundPacket packet, params int[] excludedIds) =>
         this.Enqueue(new() { Packet = packet, ExcludedIds = excludedIds }, 1);

    public void QueuePacketToLevel(ILevel level, IClientboundPacket packet, params int[] excludedIds) =>
        this.Enqueue(new() { Packet = packet, ToLevel = level, ExcludedIds = excludedIds }, 1);

    public void QueuePacketToLevel(ILevel level, int priority, IClientboundPacket packet, params int[] excludedIds) =>
        this.Enqueue(new() { Packet = packet, ExcludedIds = excludedIds, ToLevel = level }, priority);
    public void QueuePacket(IClientboundPacket packet, int priority, params int[] excludedIds) =>
        this.Enqueue(new() { Packet = packet, ExcludedIds = excludedIds }, priority);

    public void Broadcast(IClientboundPacket packet, params int[] excludedIds)
    {
        var shared = new SharedPacketBodies(packet);
        foreach (var (_, player) in this.server.OnlinePlayers)
        {
            if (!excludedIds.Contains(player.EntityId))
                Send(player, shared);
        }
    }

    public void BroadcastTo(IClientboundPacket packet, params int[] ids)
    {
        var shared = new SharedPacketBodies(packet);
        foreach (var (_, player) in this.server.OnlinePlayers)
        {
            if (ids.Contains(player.EntityId))
                Send(player, shared);
        }
    }

    public void BroadcastTo(IClientboundPacket packet, IEnumerable<IPlayer> players)
    {
        var shared = new SharedPacketBodies(packet);
        foreach (var player in players)
            Send(player, shared);
    }

    public void BroadcastToLevelInRange(ILevel toLevel, VectorD location, IClientboundPacket packet, params int[] excludedIds)
    {
        if (toLevel is not AbstractLevel world)
            return;

        var shared = new SharedPacketBodies(packet);
        foreach (var player in world.GetPlayersInRange(location, world.Configuration.EntityBroadcastRangePercentage))
        {
            if (!excludedIds.Contains(player.EntityId))
                Send(player, shared);
        }
    }

    public void QueuePacketToLevelInRange(ILevel toLevel, VectorD location, IClientboundPacket packet, params int[] excludedIds)
    {
        if (toLevel is not AbstractLevel world)
            return;

        // Who gets it is decided now: the players in range, and of those only the ones still in the level when it's sent.
        var includedIds = new List<int>();
        foreach (var player in world.GetPlayersInRange(location, world.Configuration.EntityBroadcastRangePercentage))
        {
            if (!excludedIds.Contains(player.EntityId))
                includedIds.Add(player.EntityId);
        }

        if (includedIds.Count == 0)
            return;

        this.Enqueue(new()
        {
            Packet = packet,
            ToLevel = world,
            IncludedIds = [.. includedIds],
        }, 1);
    }

    public void BroadcastToLevel(ILevel toLevel, IClientboundPacket packet, params int[] excludedIds)
    {
        if (toLevel is not AbstractLevel world)
            return;

        var shared = new SharedPacketBodies(packet);
        foreach (var (_, player) in world.Players)
        {
            if (!excludedIds.Contains(player.EntityId))
                Send(player, shared);
        }
    }

    // Sends a broadcast's packet to a player, with the bodies shared by the connections that write it alike. Other
    // players than connected ones (the server's own, tests' fakes) send it their own way.
    private static void Send(IPlayer player, SharedPacketBodies shared)
    {
        if (player is Player { Client: Client client })
            client.SendPacket(shared);
        else
            player.SendPacket(shared.Packet);
    }

    protected async override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                foreach (var queuedPacket in this.TakeBatch())
                {
                    var players = queuedPacket.ToLevel is AbstractLevel toLevel
                        ? toLevel.Players : this.server.OnlinePlayers;
                    foreach (var (_, player) in players)
                    {
                        if (ShouldGetPacket(player, queuedPacket))
                            await player.QueuePacketAsync(queuedPacket.Packet);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal service shutdown.
        }
        catch (Exception e) when (e is not ObjectDisposedException &&
            (e is not OperationCanceledException || !stoppingToken.IsCancellationRequested))
        {
            logger.LogError(e, "Packet broadcasting failed");
            await this.environment.OnServerCrashAsync(e);
        }
    }

    private static bool ShouldGetPacket(IPlayer player, QueuedPacket packet)
    {
        if (packet.IncludedIds is not null)
            return packet.IncludedIds.Contains(player.EntityId);

        return packet.ExcludedIds is null || !packet.ExcludedIds.Contains(player.EntityId);
    }

    private readonly struct QueuedPacket
    {
        public required IClientboundPacket Packet { get; init; }

        public int[]? ExcludedIds { get; init; }
        public int[]? IncludedIds { get; init; }

        public ILevel? ToLevel { get; init; }
    }
}
