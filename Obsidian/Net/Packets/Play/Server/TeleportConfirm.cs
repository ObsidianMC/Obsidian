using Obsidian.Entities;
using Obsidian.Serializer.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Server
{
    public partial class TeleportConfirm : IPacket
    {
        [Field(0), VarLength]
        public int TeleportId { get; set; }

        public int Id => 0x00;

        public TeleportConfirm() { }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public async Task ReadAsync(MinecraftStream stream)
        {
            this.TeleportId = await stream.ReadVarIntAsync();
        }

        public async Task HandleAsync(Obsidian.Server server, Player player)
        {
            if (this.TeleportId == player.TeleportId)
                return;

            await player.KickAsync("Invalid teleport... cheater?");
            //await player.TeleportAsync(player.LastLocation);//Teleport them back we didn't send this packet
        }
    }
}
