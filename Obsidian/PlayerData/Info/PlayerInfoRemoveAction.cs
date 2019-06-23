using Obsidian.Net;

using System.Threading.Tasks;

namespace Obsidian.PlayerData.Info
{
    public class PlayerInfoRemoveAction : PlayerInfoAction
    {
        public override Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;
    }
}