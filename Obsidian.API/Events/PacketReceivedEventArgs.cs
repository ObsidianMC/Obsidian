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

    /// <inheritdoc />
    /// <remarks>
    /// If true, packet will be ignored by the server.
    /// </remarks>
    public bool IsCancelled { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PacketReceivedEventArgs"/> class
    /// with <see cref="PlayerEventArgs.Player"/>, <see cref="Id"/> and <see cref="Data"/> properties set to the given values.
    /// </summary>
    /// <param name="player">The player involved in this event.</param>
    /// <param name="id">Id of the received packet.</param>
    /// <param name="data">Packet data, excluding packet id and packet length.</param>
    /// <param name="server">The server this took place in.</param>
    public PacketReceivedEventArgs(IPlayer player, IServer server, int id, ReadOnlyMemory<byte> data) : base(player, server)
    {
        Id = id;
        Data = data;
    }

    /// <inheritdoc />
    public void Cancel()
    {
        IsCancelled = true;
    }
}
