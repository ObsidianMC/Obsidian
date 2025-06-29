namespace Obsidian.API;

/// <summary>
/// Represents a network packet that can be transferred between the client and the server.
/// </summary>
public interface IPacket
{
    /// <summary>
    /// The ID of the packet.
    /// </summary>
    public int Id { get; }
}

/// <summary>
/// Represents a network packet that can be sent to the client by the server.
/// </summary>
public interface IClientboundPacket : IPacket
{
    /// <summary>
    /// Writes the data content of the packet to the stream.
    /// </summary>
    /// <param name="writer">The stream writer to write the data to.</param>
    public void Serialize(INetStreamWriter writer);
}
