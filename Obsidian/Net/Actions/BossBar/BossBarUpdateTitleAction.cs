using System.Threading.Tasks;

using Obsidian.Chat;

namespace Obsidian.Net.Actions.BossBar
{
    public class BossBarUpdateTitleAction : BossBarAction
    {
        public override int Action => 3;
        public ChatMessage Title { get; set; }

        public override byte[] ToArray()
        {
            using var stream = new MinecraftStream();
            stream.WriteChat(Title);

            return stream.ToArray();
        }

        public override async Task<byte[]> ToArrayAsync()
        {
            using var stream = new MinecraftStream();
            await stream.WriteChatAsync(Title);

            return stream.ToArray();
        }
    }
}
