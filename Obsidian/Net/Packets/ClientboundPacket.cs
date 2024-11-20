using Org.BouncyCastle.Bcpg;
using System.IO;

namespace Obsidian.Net.Packets;

public abstract class ClientboundPacket : IClientboundPacket
{
    public abstract int Id { get; }

    public virtual void Serialize(INetStreamWriter writer) { }
}
