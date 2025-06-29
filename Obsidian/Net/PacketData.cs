namespace Obsidian.Net;
public readonly struct PacketData
{
    public static readonly PacketData Default = new() { Id = -1, NetworkBuffer = new() };

    public required int Id { get; init; }

    public NetworkBuffer NetworkBuffer { get; init; }

    public void Deconstruct(out int id, out NetworkBuffer networkBuffer)
    {
        id = this.Id;
        networkBuffer = this.NetworkBuffer;
    }

}
