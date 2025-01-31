using Org.BouncyCastle.Bcpg;
using System.IO;

namespace Obsidian.Net.Packets;

public abstract class ClientboundPacket : IClientboundPacket
{
    public abstract int Id { get; }

    /// <summary>
    /// Writes the data content of the packet to the given stream.
    /// </summary>
    /// <param name="writer"></param>
    public virtual void Serialize(INetStreamWriter writer) { }
}
