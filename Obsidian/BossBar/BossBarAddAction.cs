using System.IO;
using System.Threading.Tasks;

namespace Obsidian.BossBar
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
            using (var stream = new MemoryStream())
            {
                await stream.WriteAutoAsync(Title, Health, Color, Division, (byte)Flags);
                return stream.ToArray();
            }
        }
    }
}
