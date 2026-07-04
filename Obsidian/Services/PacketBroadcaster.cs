using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Obsidian.API.World;
using Obsidian.Entities;
using Obsidian.Hosting;
using Obsidian.WorldData;
using System.Threading;

namespace Obsidian.Services;

public sealed class PacketBroadcaster(IServer server, ILogger<PacketBroadcaster> logger, IServerEnvironment environment) : BackgroundService, IPacketBroadcaster
{
    private readonly IServer server = server;
    private readonly IServerEnvironment environment = environment;
    private readonly PriorityQueue<QueuedPacket, int> priorityQueue = new();

    public void QueuePacketTo(IClientboundPacket packet, params int[] ids)
    {
        this.priorityQueue.Enqueue(new() { Packet = packet, IncludedIds = ids }, 1);
    }

    public void QueuePacketTo(IClientboundPacket packet, int priority, params int[] ids)
    {
        this.priorityQueue.Enqueue(new()
        {
            Packet = packet,
            IncludedIds = ids
        }, priority);
    }

    public void QueuePacket(IClientboundPacket packet, params int[] excludedIds) =>
         this.priorityQueue.Enqueue(new() { Packet = packet, ExcludedIds = excludedIds }, 1);

    public void QueuePacketToLevel(ILevel level, IClientboundPacket packet, params int[] excludedIds) =>
        this.priorityQueue.Enqueue(new() { Packet = packet, ToLevel = level, ExcludedIds = excludedIds }, 1);

    public void QueuePacketToLevel(ILevel level, int priority, IClientboundPacket packet, params int[] excludedIds) =>
        this.priorityQueue.Enqueue(new() { Packet = packet, ExcludedIds = excludedIds, ToLevel = level }, priority);
    public void QueuePacket(IClientboundPacket packet, int priority, params int[] excludedIds) =>
        this.priorityQueue.Enqueue(new() { Packet = packet, ExcludedIds = excludedIds }, priority);

    public void Broadcast(IClientboundPacket packet, params int[] excludedIds)
    {
        foreach (var player in this.server.OnlinePlayers.Values.Where(player => !excludedIds.Contains(player.EntityId)))
            player.Client.SendPacket(packet);
    }

    public void BroadcastTo(IClientboundPacket packet, params int[] ids)
    {
        foreach (var player in this.server.OnlinePlayers.Values.Where(player => ids.Contains(player.EntityId)))
            player.Client.SendPacket(packet);
    }

    public void BroadcastToLevelInRange(ILevel toLevel, VectorF location, IClientboundPacket packet, params int[] excludedIds)
    {
        if (toLevel is not AbstractLevel world)
            return;

        foreach (var player in world.GetPlayersInRange(location, world.Configuration.EntityBroadcastRangePercentage).Cast<Player>().Where(x => !excludedIds.Contains(x.EntityId)))
            player.Client.SendPacket(packet);
    }

    public void QueuePacketToLevelInRange(ILevel toLevel, VectorF location, IClientboundPacket packet, params int[] excludedIds)
    {
        if (toLevel is not AbstractLevel world)
            return;

        var includedIDs = world.GetPlayersInRange(location, world.Configuration.EntityBroadcastRangePercentage)
            .Select(x => x.EntityId)
            .ToHashSet();

        excludedIds = excludedIds.Concat(world.Players.Values
            .Select(x => x.EntityId)
            .Where(x => !includedIDs.Contains(x)))
            .ToArray();

        this.priorityQueue.Enqueue(new()
        {
            Packet = packet,
            ToLevel = world,
            ExcludedIds = excludedIds,
        }, 1);
    }


    public void BroadcastToLevel(ILevel toLevel, IClientboundPacket packet, params int[] excludedIds)
    {
        if (toLevel is not AbstractLevel world)
            return;

        foreach (var player in world.Players.Values.Where(player => !excludedIds.Contains(player.EntityId)))
            player.Client.SendPacket(packet);
    }

    protected async override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                if (!this.priorityQueue.TryDequeue(out var queuedPacket, out var priority))
                    continue;

                if (queuedPacket.ToLevel is AbstractLevel toLevel)
                {
                    foreach (var player in toLevel.Players.Values.Where(player => ShouldGetPacket(player, queuedPacket)))
                        await player.Client.QueuePacketAsync(queuedPacket.Packet);

                    continue;
                }

                foreach (var player in this.server.OnlinePlayers.Values.Where(player => ShouldGetPacket(player, queuedPacket)))
                    await player.Client.QueuePacketAsync(queuedPacket.Packet);

            }
        }
        catch (Exception e) when (e is not OperationCanceledException or ObjectDisposedException)
        {
            await this.environment.OnServerCrashAsync(e);
        }
    }

    private static bool ShouldGetPacket(IPlayer player, QueuedPacket packet)
    {
        if (packet.IncludedIds != null)
            return packet.IncludedIds.Contains(player.EntityId);

        return packet.ExcludedIds == null || !packet.ExcludedIds.Contains(player.EntityId);
    }

    private readonly struct QueuedPacket
    {
        public required IClientboundPacket Packet { get; init; }

        public int[]? ExcludedIds { get; init; }
        public int[]? IncludedIds { get; init; }

        public ILevel? ToLevel { get; init; }
    }
}
