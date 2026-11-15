namespace Obsidian.API;
/// <summary>
/// Sends packets to several players at once. Broadcast methods send right away; queue methods hold the packet until
/// the next batch, sent every 20 milliseconds through each player's outgoing packet handlers.
/// </summary>
public interface IPacketBroadcaster
{
    /// <summary>
    /// Sends the packets directly to connected clients without processing in a queue.
    /// </summary>
    /// <param name="packet">The packet to send.</param>
    /// <param name="excludedIds">The list of entity ids to exlude from the broadcast.</param>
    public void Broadcast(IClientboundPacket packet, params int[] excludedIds);
    /// <summary>
    /// Sends the packet directly to the online players with the given entity ids.
    /// </summary>
    /// <param name="packet">The packet to send.</param>
    /// <param name="ids">The entity ids of the players to send it to.</param>
    public void BroadcastTo(IClientboundPacket packet, params int[] ids);

    /// <summary>
    /// Sends the packet directly to <paramref name="players"/>, like <see cref="IPlayer.SendPacket"/> for each. The
    /// server's broadcaster serializes it once for all the players' connections that write it alike.
    /// </summary>
    /// <param name="packet">The packet to send.</param>
    /// <param name="players">The players to send it to, enumerated once.</param>
    public void BroadcastTo(IClientboundPacket packet, IEnumerable<IPlayer> players)
    {
        foreach (var player in players)
            player.SendPacket(packet);
    }

    /// <summary>
    /// Sends the packets directly to connected clients without processing in a queue.
    /// </summary>
    /// <param name="toLevel">The level to broadcast this packet to.</param>
    /// <param name="packet">The packet to send.</param>
    /// <param name="excludedIds">The list of entity ids to exlude from the broadcast.</param>
    public void BroadcastToLevel(ILevel toLevel, IClientboundPacket packet, params int[] excludedIds);

    /// <summary>
    /// Sends the packet directly to the players in <paramref name="level"/> within its entity broadcast range of
    /// <paramref name="location"/>, measured horizontally.
    /// </summary>
    /// <param name="level">The level whose players get the packet.</param>
    /// <param name="location">The point the range is measured from.</param>
    /// <param name="packet">The packet to send.</param>
    /// <param name="excludedIds">The entity ids of players not to send it to.</param>
    public void BroadcastToLevelInRange(ILevel level, VectorD location, IClientboundPacket packet, params int[] excludedIds);

    /// <summary>
    /// Queues the packet for the online players with the given entity ids, with a priority of 1.
    /// </summary>
    /// <param name="packet">The packet to send.</param>
    /// <param name="ids">The entity ids of the players to send it to.</param>
    public void QueuePacketTo(IClientboundPacket packet, params int[] ids);
    /// <summary>
    /// Queues the packet for the online players with the given entity ids.
    /// </summary>
    /// <param name="packet">The packet to send.</param>
    /// <param name="priority">The packet's place in its batch: lower values are sent first.</param>
    /// <param name="ids">The entity ids of the players to send it to.</param>
    public void QueuePacketTo(IClientboundPacket packet, int priority, params int[] ids);
    /// <summary>
    /// Puts the packet in a priority queue for processing then broadcasting when dequeued.
    /// </summary>
    /// <param name="toLevel">The level to broadcast this packet to.</param>
    /// <param name="packet">The packet to send.</param>
    /// <param name="excludedIds">The list of entity ids to exlude from the broadcast.</param>
    /// <remarks>Packets queued without a priority set will be queued up with a priority of 1.</remarks>
    public void QueuePacketToLevel(ILevel toLevel, IClientboundPacket packet, params int[] excludedIds);

    /// <summary>
    /// Queues the packet for the players in <paramref name="level"/> within its entity broadcast range of
    /// <paramref name="location"/>, measured horizontally. The players are picked now, and only those still in the level
    /// get it.
    /// </summary>
    /// <param name="level">The level whose players get the packet.</param>
    /// <param name="location">The point the range is measured from.</param>
    /// <param name="packet">The packet to send.</param>
    /// <param name="excludedIds">The entity ids of players not to send it to.</param>
    public void QueuePacketToLevelInRange(ILevel level, VectorD location, IClientboundPacket packet, params int[] excludedIds);

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
    /// <param name="priority">The packet's place in its batch: lower values are sent first.</param>
    /// <param name="packet">The packet to send.</param>
    /// <param name="excludedIds">The list of entity ids to exlude from the broadcast.</param>
    public void QueuePacketToLevel(ILevel toLevel, int priority, IClientboundPacket packet, params int[] excludedIds);

    /// <summary>
    /// Puts the packet in a priority queue for processing then broadcasting when dequeued.
    /// </summary>
    /// <param name="priority">The packet's place in its batch: lower values are sent first.</param>
    /// <param name="packet">The packet to send.</param>
    /// <param name="excludedIds">The list of entity ids to exlude from the broadcast.</param>
    public void QueuePacket(IClientboundPacket packet, int priority, params int[] excludedIds);
}
