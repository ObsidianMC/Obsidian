using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class ClickWindowButton : IServerboundPacket
{
    [Field(0)]
    public sbyte WindowId { get; private set; }

    [Field(1)]
    public sbyte ButtonId { get; private set; }

    public int Id => 0x07;

    public void Populate(byte[] data) => throw new NotImplementedException();

    public void Populate(MinecraftStream stream) => throw new NotImplementedException();

    public ValueTask HandleAsync(Server server, Player player) => ValueTask.CompletedTask;
}
