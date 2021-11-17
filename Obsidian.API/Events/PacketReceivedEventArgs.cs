namespace Obsidian.API.Events;

public sealed class PacketReceivedEventArgs : PlayerEventArgs, ICancellable
{
    /// <summary>
    /// Id of the received packet.
    /// </summary>
    public int Id { get; }

    /// <summary>
    /// Packet data, excluding packet id and packet length.
    /// </summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>
    /// If true, packet will be ignored by the server.
    /// </summary>
    public bool Cancel { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PacketReceivedEventArgs"/> class
    /// with <see cref="PlayerEventArgs.Player"/>, <see cref="Id"/> and <see cref="Data"/> properties set to the given values.
    /// </summary>
    /// <param name="player">The player involved in this event.</param>
    /// <param name="id">Id of the received packet.</param>
    /// <param name="data">Packet data, excluding packet id and packet length.</param>
    public PacketReceivedEventArgs(IPlayer player, int id, ReadOnlyMemory<byte> data) : base(player)
    {
        Id = id;
        Data = data;
    }
}
