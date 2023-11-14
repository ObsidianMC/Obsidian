using Obsidian.Entities;

namespace Obsidian.Net.Packets.Configuration.Serverbound;
public sealed partial class FinishConfigurationPacket : IServerboundPacket
{
    public int Id => 0x02;

    public async ValueTask HandleAsync(Server server, Player player) =>
         await player.client.ConnectAsync();

    public void Populate(byte[] data) { }
    public void Populate(MinecraftStream stream) { }
}
