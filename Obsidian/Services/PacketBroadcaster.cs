using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Obsidian.Entities;
using Obsidian.Net.Packets;
using Obsidian.WorldData;
using System.Threading;

namespace Obsidian.Services;
public sealed class PacketBroadcaster : BackgroundService, IPacketBroadcaster
{
    private readonly IServer server;
    private readonly PriorityQueue<QueuedPacket, int> priorityQueue = new();
    private readonly ILogger logger;

    public PacketBroadcaster(IServer server, ILoggerFactory loggerFactory)
    {
        this.server = server;
        this.logger = loggerFactory.CreateLogger<PacketBroadcaster>();
    }

    public void QueuePacket(IClientboundPacket packet, params int[] exluded) =>
         this.priorityQueue.Enqueue(new() { Packet = packet, ExcludedIds = exluded }, 1);

    public void QueuePacketToWorld(IWorld world, IClientboundPacket packet, params int[] exluded) =>
        this.priorityQueue.Enqueue(new() { Packet = packet, ExcludedIds = exluded }, 1);

    public void QueuePacketToWorld(IWorld world, int priority, IClientboundPacket packet, params int[] exluded) =>
        this.priorityQueue.Enqueue(new() { Packet = packet, ExcludedIds = exluded, ToWorld = world }, priority);

    public void QueuePacket(IClientboundPacket packet, int priority, params int[] exluded) =>
        this.priorityQueue.Enqueue(new() { Packet = packet, ExcludedIds = exluded }, priority);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!this.priorityQueue.TryDequeue(out var queuedPacket, out _))
                continue;

            if (queuedPacket.ToWorld is World toWorld)
            {
                foreach (var player in toWorld.Players.Values.Where(player => !queuedPacket.ExcludedIds.Contains(player.EntityId)))
                    await player.client.QueuePacketAsync(queuedPacket.Packet);

                continue;
            }

            foreach (var player in this.server.Players.Cast<Player>().Where(player => !queuedPacket.ExcludedIds.Contains(player.EntityId)))
                await player.client.QueuePacketAsync(queuedPacket.Packet);
        }
    }

    private readonly struct QueuedPacket
    {
        public required IClientboundPacket Packet { get; init; }

        public int[] ExcludedIds { get; init; }

        public IWorld? ToWorld { get; init; }
    }
}

public interface IPacketBroadcaster
{
    public void QueuePacketToWorld(IWorld world, IClientboundPacket packet, params int[] exluded);
    public void QueuePacket(IClientboundPacket packet, params int[] exluded);
    public void QueuePacketToWorld(IWorld world, int priority, IClientboundPacket packet, params int[] exluded);
    public void QueuePacket(IClientboundPacket packet, int priority, params int[] exluded);
}
