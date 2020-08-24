using Obsidian.Net;
using System.Threading.Tasks;

namespace Obsidian.Boss
{
    public class BossBarUpdateFlagsAction : BossBarAction
    {
        public override int Action => 5;
        public BossBarFlags Flags { get; set; }

        public override async Task<byte[]> ToArrayAsync()
        {
            using (var stream = new MinecraftStream())
            {
                await stream.WriteUnsignedByteAsync((byte)Flags);

                return stream.ToArray();
            }
        }
    }
}
