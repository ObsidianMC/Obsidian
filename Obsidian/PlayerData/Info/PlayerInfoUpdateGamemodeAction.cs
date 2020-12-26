using Obsidian.Net;
using System.Threading.Tasks;

namespace Obsidian.PlayerData.Info
{
    public class PlayerInfoUpdateGamemodeAction : PlayerInfoAction
    {
        public int Gamemode { get; set; }

        public override async Task WriteAsync(MinecraftStream stream)
        {
            await base.WriteAsync(stream);

            await stream.WriteVarIntAsync(this.Gamemode);
        }

        public override void Write(MinecraftStream stream)
        {
            base.Write(stream);

            stream.WriteVarInt(Gamemode);
        }
    }
}