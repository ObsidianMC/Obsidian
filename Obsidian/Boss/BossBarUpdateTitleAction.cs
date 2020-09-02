using Obsidian.Chat;
using Obsidian.Net;
using System.Threading.Tasks;

namespace Obsidian.Boss
{
    public class BossBarUpdateTitleAction : BossBarAction
    {
        public override int Action => 3;
        public ChatMessage Title { get; set; }
        public override async Task<byte[]> ToArrayAsync()
        {
            using var stream = new MinecraftStream();
            await stream.WriteChatAsync(Title);

            return stream.ToArray();
        }
    }
}
