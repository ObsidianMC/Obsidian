using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class AcceptTeleportationPacket
{
    [Field(0), VarLength]
    public int TeleportId { get; private set; }

    public override void Populate(INetStreamReader reader)
    {
        TeleportId = reader.ReadVarInt();
    }

    public async override ValueTask HandleAsync(Server server, Player player)
    {
        if (TeleportId == player.TeleportId)
            return;

        await player.KickAsync("Invalid teleport... cheater?");
        //await player.TeleportAsync(player.LastLocation); // Teleport them back we didn't send this packet
    }
}
