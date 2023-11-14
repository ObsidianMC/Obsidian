using Obsidian.Entities;

namespace Obsidian.Net.Packets.Configuration;
public sealed partial class FinishConfigurationPacket : IServerboundPacket, IClientboundPacket
{
    public static FinishConfigurationPacket Default { get; } = new();

    public int Id => 0x02;

    //TODO move connect logic into here
    public async ValueTask HandleAsync(Server server, Player player) =>
         await player.client.ConnectAsync();

    public void Populate(byte[] data) { }
    public void Populate(MinecraftStream stream) { }
}
