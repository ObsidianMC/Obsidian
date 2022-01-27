using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play;

public partial class WindowConfirmation : IClientboundPacket, IServerboundPacket
{
    [Field(0)]
    public sbyte WindowId { get; private set; }

    [Field(1)]
    public short ActionNumber { get; private set; }

    [Field(2)]
    public bool Accepted { get; private set; }

    public int Id => 0x11;
    public void Serialize(MinecraftStream stream) => throw new NotImplementedException();

    public void Populate(byte[] data) => throw new NotImplementedException();

    public void Populate(MinecraftStream stream) => throw new NotImplementedException();

    public ValueTask HandleAsync(Server server, Player player) => ValueTask.CompletedTask;
}
