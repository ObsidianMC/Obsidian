using Obsidian.Net;
using System.Threading.Tasks;

namespace Obsidian.PlayerData.Info
{
    public class PlayerInfoUpdateDisplayNameAction : PlayerInfoAction
    {
        public string DisplayName { get; set; }
        public bool HasDisplayName => string.IsNullOrWhiteSpace(DisplayName);

        public override async Task WriteAsync(MinecraftStream stream)
        {
            await base.WriteAsync(stream);

            await stream.WriteStringAsync(this.DisplayName);
        }
    }
}