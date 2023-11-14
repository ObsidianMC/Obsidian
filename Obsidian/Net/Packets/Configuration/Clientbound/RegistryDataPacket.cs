using Obsidian.Nbt;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Configuration.Clientbound;
public sealed partial class RegistryDataPacket : IClientboundPacket
{
    public static RegistryDataPacket Default { get; } = new();

    [Field(0)]
    public MixedCodec Codec { get; init; } = new();

    public int Id => 0x05;

    public void Serialize(MinecraftStream stream)
    {

    }
}
