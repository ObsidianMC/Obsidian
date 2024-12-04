using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Obsidian.Entities;
using Obsidian.Hosting;
using Obsidian.Net.Packets;
using Obsidian.WorldData;
using System.Threading;

namespace Obsidian.Services;
public sealed class PacketBroadcaster : BackgroundService, IPacketBroadcaster
{
    private readonly IServer server;
    private readonly IServerEnvironment environment;
    private readonly PriorityQueue<QueuedPacket, int> priorityQueue = new();
    private readonly ILogger logger;

    public PacketBroadcaster(IServer server, ILoggerFactory loggerFactory, IServerEnvironment environment)
    {
        this.server = server;
        this.environment = environment;
        this.logger = loggerFactory.CreateLogger<PacketBroadcaster>();
    }

    public void QueuePacket(IClientboundPacket packet, params int[] excludedIds) =>
         this.priorityQueue.Enqueue(new() { Packet = packet, ExcludedIds = excludedIds }, 1);

    public void QueuePacketToWorld(IWorld world, IClientboundPacket packet, params int[] excludedIds)
    {
        this.priorityQueue.Enqueue(new() { Packet = packet, ToWorld = world, ExcludedIds = excludedIds }, 1);
    }

    public void QueuePacketToWorld(IWorld world, int priority, IClientboundPacket packet, params int[] excludedIds) =>
        this.priorityQueue.Enqueue(new() { Packet = packet, ExcludedIds = excludedIds, ToWorld = world }, priority);

    public void QueuePacket(IClientboundPacket packet, int priority, params int[] excludedIds) =>
        this.priorityQueue.Enqueue(new() { Packet = packet, ExcludedIds = excludedIds }, priority);

    public void Broadcast(IClientboundPacket packet, params int[] excludedIds)
    {
        foreach (var player in this.server.Players.Cast<Player>().Where(player => !excludedIds.Contains(player.EntityId)))
            player.client.SendPacket(packet);
    }

    public void BroadcastToWorldInRange(IWorld toWorld, VectorF location, IClientboundPacket packet, params int[] excludedIds)
    {
        if (toWorld is not World world)
            return;

        foreach (var player in world.GetPlayersInRange(location, world.Configuration.EntityBroadcastRangePercentage))
            player.client.SendPacket(packet);
    }

    public void QueuePacketToWorldInRange(IWorld toWorld, VectorF location, IClientboundPacket packet, params int[] excludedIds)
    {
        if (toWorld is not World world)
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
            Packet =packet,
            ToWorld = world,
            ExcludedIds = excludedIds,
        }, 1);
    }


    public void BroadcastToWorld(IWorld toWorld, IClientboundPacket packet, params int[] excludedIds)
    {
        if (toWorld is not World world)
            return;

        foreach (var player in world.Players.Values.Where(player => !excludedIds.Contains(player.EntityId)))
            player.client.SendPacket(packet);
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

                if (queuedPacket.ToWorld is World toWorld)
                {
                    foreach (var player in toWorld.Players.Values.Where(player => queuedPacket.ExcludedIds != null && !queuedPacket.ExcludedIds.Contains(player.EntityId)))
                        await player.client.QueuePacketAsync(queuedPacket.Packet);

                    continue;
                }

                foreach (var player in this.server.Players.Cast<Player>().Where(player => queuedPacket.ExcludedIds != null && !queuedPacket.ExcludedIds.Contains(player.EntityId)))
                    await player.client.QueuePacketAsync(queuedPacket.Packet);

            }
        }
        catch (Exception e) when (e is not OperationCanceledException or ObjectDisposedException)
        {
            await this.environment.OnServerCrashAsync(e);
        }
    }

    private readonly struct QueuedPacket
    {
        public required IClientboundPacket Packet { get; init; }

        public int[]? ExcludedIds { get; init; }

        public IWorld? ToWorld { get; init; }
    }
}

public interface IPacketBroadcaster
{
    /// <summary>
    /// Sends the packets directly to connected clients without processing in a queue.
    /// </summary>
    /// <param name="packet">The packet to send.</param>
    /// <param name="excludedIds">The list of entity ids to exlude from the broadcast.</param>
    public void Broadcast(IClientboundPacket packet, params int[] excludedIds);

    /// <summary>
    /// Sends the packets directly to connected clients without processing in a queue.
    /// </summary>
    /// <param name="toWorld">The world to broadcast this packet to.</param>
    /// <param name="packet">The packet to send.</param>
    /// <param name="excludedIds">The list of entity ids to exlude from the broadcast.</param>
    public void BroadcastToWorld(IWorld toWorld, IClientboundPacket packet, params int[] excludedIds);

    public void BroadcastToWorldInRange(IWorld world, VectorF location, IClientboundPacket packet, params int[] excludedIds);

    /// <summary>
    /// Puts the packet in a priority queue for processing then broadcasting when dequeued.
    /// </summary>
    /// <param name="toWorld">The world to broadcast this packet to.</param>
    /// <param name="packet">The packet to send.</param>
    /// <param name="excludedIds">The list of entity ids to exlude from the broadcast.</param>
    /// /// <remarks>Packets queued without a priority set will be queued up with a priority of 1.</remarks>
    public void QueuePacketToWorld(IWorld toWorld, IClientboundPacket packet, params int[] excludedIds);

    public void QueuePacketToWorldInRange(IWorld world, VectorF location, IClientboundPacket packet, params int[] excludedIds);

    /// <summary>
    /// Puts the packet in a priority queue for processing then broadcasting when dequeued.
    /// </summary>
    /// <param name="packet">The packet to send.</param>
    /// <param name="excludedIds">The list of entity ids to exlude from the broadcast.</param>
    /// <remarks>Packets queued without a priority set will be queued up with a priority of 1.</remarks>
    public void QueuePacket(IClientboundPacket packet, params int[] excludedIds);

    /// <summary>
    /// Puts the packet in a priority queue for processing then broadcasting when dequeued.
    /// </summary>
    /// <param name="toWorld">The world to broadcast this packet to.</param>
    /// <param name="priority">The priority to set the packet in the queue. Higher priority = better</param>
    /// <param name="packet">The packet to send.</param>
    /// <param name="excludedIds">The list of entity ids to exlude from the broadcast.</param>
    public void QueuePacketToWorld(IWorld toWorld, int priority, IClientboundPacket packet, params int[] excludedIds);

    /// <summary>
    /// Puts the packet in a priority queue for processing then broadcasting when dequeued.
    /// </summary>
    /// <param name="priority">The priority to set the packet in the queue. Higher priority = better</param>
    /// <param name="packet">The packet to send.</param>
    /// <param name="excludedIds">The list of entity ids to exlude from the broadcast.</param>
    public void QueuePacket(IClientboundPacket packet, int priority, params int[] excludedIds);
}
