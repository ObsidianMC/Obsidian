namespace Obsidian.API;
public interface IPacketBroadcaster
{
    /// <summary>
    /// Sends the packets directly to connected clients without processing in a queue.
    /// </summary>
    /// <param name="packet">The packet to send.</param>
    /// <param name="excludedIds">The list of entity ids to exlude from the broadcast.</param>
    public void Broadcast(IClientboundPacket packet, params int[] excludedIds);
    public void BroadcastTo(IClientboundPacket packet, params int[] ids);

    /// <summary>
    /// Sends the packets directly to connected clients without processing in a queue.
    /// </summary>
    /// <param name="toLevel">The level to broadcast this packet to.</param>
    /// <param name="packet">The packet to send.</param>
    /// <param name="excludedIds">The list of entity ids to exlude from the broadcast.</param>
    public void BroadcastToLevel(ILevel toLevel, IClientboundPacket packet, params int[] excludedIds);

    public void BroadcastToLevelInRange(ILevel level, VectorF location, IClientboundPacket packet, params int[] excludedIds);

    public void QueuePacketTo(IClientboundPacket packet, params int[] ids);
    public void QueuePacketTo(IClientboundPacket packet, int priority, params int[] ids);
    /// <summary>
    /// Puts the packet in a priority queue for processing then broadcasting when dequeued.
    /// </summary>
    /// <param name="toLevel">The level to broadcast this packet to.</param>
    /// <param name="packet">The packet to send.</param>
    /// <param name="excludedIds">The list of entity ids to exlude from the broadcast.</param>
    /// /// <remarks>Packets queued without a priority set will be queued up with a priority of 1.</remarks>
    public void QueuePacketToLevel(ILevel toLevel, IClientboundPacket packet, params int[] excludedIds);

    public void QueuePacketToLevelInRange(ILevel level, VectorF location, IClientboundPacket packet, params int[] excludedIds);

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
    /// <param name="toLevel">The level to broadcast this packet to.</param>
    /// <param name="priority">The priority to set the packet in the queue. Higher priority = better</param>
    /// <param name="packet">The packet to send.</param>
    /// <param name="excludedIds">The list of entity ids to exlude from the broadcast.</param>
    public void QueuePacketToLevel(ILevel toLevel, int priority, IClientboundPacket packet, params int[] excludedIds);

    /// <summary>
    /// Puts the packet in a priority queue for processing then broadcasting when dequeued.
    /// </summary>
    /// <param name="priority">The priority to set the packet in the queue. Higher priority = better</param>
    /// <param name="packet">The packet to send.</param>
    /// <param name="excludedIds">The list of entity ids to exlude from the broadcast.</param>
    public void QueuePacket(IClientboundPacket packet, int priority, params int[] excludedIds);
}
