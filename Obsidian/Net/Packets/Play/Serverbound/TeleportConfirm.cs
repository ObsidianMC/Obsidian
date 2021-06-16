using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Serverbound
{
    public partial class TeleportConfirm : IServerboundPacket
    {
        [Field(0), VarLength]
        public int TeleportId { get; private set; }

        public int Id => 0x00;

        public async ValueTask HandleAsync(Server server, Player player)
        {
            if (TeleportId == player.TeleportId)
                return;

            await player.KickAsync("Invalid teleport... cheater?");
            //await player.TeleportAsync(player.LastLocation); // Teleport them back we didn't send this packet
        }
    }
}
