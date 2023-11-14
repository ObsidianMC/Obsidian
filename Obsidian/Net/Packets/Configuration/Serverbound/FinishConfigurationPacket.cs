using Obsidian.Entities;

namespace Obsidian.Net.Packets.Configuration.Serverbound;
public sealed partial class FinishConfigurationPacket : IServerboundPacket
{
    public int Id => 0x02;

    public ValueTask HandleAsync(Server server, Player player)
    {
        player.client.State = ClientState.Play;

        return ValueTask.CompletedTask;
    }

    public void Populate(byte[] data) { }
    public void Populate(MinecraftStream stream) { }
}
