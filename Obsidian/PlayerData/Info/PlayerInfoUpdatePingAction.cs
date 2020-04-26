using Obsidian.Net;
using System;
using System.Threading.Tasks;

namespace Obsidian.PlayerData.Info
{
    public class PlayerInfoUpdatePingAction : PlayerInfoAction
    {
        public int Ping { get; set; }

        public override async Task WriteAsync(MinecraftStream stream)
        {
            await stream.WriteUuidAsync(Guid.Parse(this.Uuid));
            await stream.WriteVarIntAsync(this.Ping);
        }
    }
}