namespace Obsidian.Net.Packets;
public abstract record class CommonPacket : IClientboundPacket, IServerboundPacket
{
    public virtual int Id { get; init; }

    /// <summary>
    /// The protocol the packet's id is for, set by its generated factories (<c>ClientboundPlay</c>, say).
    /// </summary>
    public ClientState State { get; init; }

    public virtual void Serialize(INetStreamWriter writer) { }

    public virtual void Populate(INetStreamReader reader) { }
    public virtual ValueTask HandleAsync(IServer server, IClientPlayer player) => default;
    public virtual ValueTask HandleAsync(IClient client) => default;
}
