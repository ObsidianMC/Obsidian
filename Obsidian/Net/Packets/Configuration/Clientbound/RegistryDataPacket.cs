using Obsidian.Nbt;

namespace Obsidian.Net.Packets.Configuration.Clientbound;
public sealed class RegistryDataPacket : IClientboundPacket
{
    public int Id => 0x05;

    public required NbtCompound RegistryCodec { get; init; }

    public void Serialize(MinecraftStream stream)
    {

    }
}
