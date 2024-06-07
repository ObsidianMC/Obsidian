using Obsidian.Entities;

namespace Obsidian.Net.Packets.Play.Serverbound;
public sealed partial class AcknowledgeConfiguration : IServerboundPacket
{
    public int Id => 0x0C;

    //TODO handle 
    public ValueTask HandleAsync(Server server, Player player) => ValueTask.CompletedTask;
    public void Populate(byte[] data) { }
    public void Populate(MinecraftStream stream) { }
}
