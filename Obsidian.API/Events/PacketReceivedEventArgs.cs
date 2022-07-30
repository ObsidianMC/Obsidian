namespace Obsidian.API.Events;

public sealed class PacketReceivedEventArgs : BaseEventArgs, IPlayerEvent, ICancellable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PacketReceivedEventArgs"/> class
    /// with <see cref="IPlayerEvent.Player"/>, <see cref="Id"/> and <see cref="Data"/> properties set to the given values.
    /// </summary>
    /// <param name="player">The player involved in this event.</param>
    /// <param name="id">Id of the received packet.</param>
    /// <param name="data">Packet data, excluding packet id and packet length.</param>
    internal PacketReceivedEventArgs(IPlayer player, int id, ReadOnlyMemory<byte> data)
    {
        Player = player;
        Id = id;
        Data = data;
    }

    /// <summary>
    /// Id of the received packet.
    /// </summary>
    public int Id { get; }

    /// <summary>
    /// Packet data, excluding packet id and packet length.
    /// </summary>
    public ReadOnlyMemory<byte> Data { get; }

    public IPlayer Player { get; }
    public bool Cancelled { get; set; }
}
