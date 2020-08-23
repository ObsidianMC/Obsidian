using Obsidian.Net;
using System;
using System.Threading.Tasks;

namespace Obsidian.PlayerData.Info
{
    public class PlayerInfoUpdateDisplayNameAction : PlayerInfoAction
    {
        public string DisplayName { get; set; }
        public bool HasDisplayName => string.IsNullOrWhiteSpace(DisplayName);

        public override async Task WriteAsync(MinecraftStream stream)
        {
            await stream.WriteUuidAsync(this.Uuid);
            await stream.WriteStringAsync(this.DisplayName);
        }
    }
}