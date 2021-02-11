using System.Threading.Tasks;

using Obsidian.BossBar;

namespace Obsidian.Net.Actions.BossBar
{
    public class BossBarUpdateFlagsAction : BossBarAction
    {
        public override int Action => 5;

        public BossBarFlags Flags { get; set; }

        public override byte[] ToArray()
        {
            using var stream = new MinecraftStream();
            stream.WriteUnsignedByte((byte)Flags);
            return stream.ToArray();
        }

        public override async Task<byte[]> ToArrayAsync()
        {
            using var stream = new MinecraftStream();
            await stream.WriteUnsignedByteAsync((byte)Flags);

            return stream.ToArray();
        }
    }
}
