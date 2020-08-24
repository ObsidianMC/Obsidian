using Obsidian.Net;
using System;
using System.Threading.Tasks;

namespace Obsidian.PlayerData.Info
{
    public class PlayerInfoUpdateGamemodeAction : PlayerInfoAction
    {
        public int Gamemode { get; set; }

        public override async Task WriteAsync(MinecraftStream stream)
        {
            await stream.WriteUuidAsync(Guid.Parse(this.Uuid));
            await stream.WriteVarIntAsync(this.Gamemode);
        }
    }
}