using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Serverbound
{
    [ServerOnly]
    public partial class TeleportConfirm : IPacket
    {
        [Field(0), VarLength]
        public int TeleportId { get; set; }

        public int Id => 0x00;

        public async Task ReadAsync(MinecraftStream stream)
        {
            this.TeleportId = await stream.ReadVarIntAsync();
        }

        public async Task HandleAsync(Server server, Player player)
        {
            if (this.TeleportId == player.TeleportId)
                return;

            await player.KickAsync("Invalid teleport... cheater?");
            //await player.TeleportAsync(player.LastLocation);//Teleport them back we didn't send this packet
        }
    }
}
