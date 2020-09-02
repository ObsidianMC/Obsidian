using Obsidian.Net;
using System.Threading.Tasks;

namespace Obsidian.Boss
{
    public class BossBarAddAction : BossBarAction
    {
        public override int Action => 0;

        public Chat.ChatMessage Title { get; set; }

        public float Health { get; set; }

        public BossBarColor Color { get; set; }

        public BossBarDivisionType Division { get; set; }

        public BossBarFlags Flags { get; set; }

        public override async Task<byte[]> ToArrayAsync()
        {
            using var stream = new MinecraftStream();
            await stream.WriteChatAsync(Title);
            await stream.WriteFloatAsync(Health);
            await stream.WriteVarIntAsync(Color);
            await stream.WriteVarIntAsync(Division);
            await stream.WriteUnsignedByteAsync((byte)Flags);
            return stream.ToArray();
        }
    }
}
