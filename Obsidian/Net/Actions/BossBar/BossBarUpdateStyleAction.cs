using System.Threading.Tasks;

using Obsidian.BossBar;

namespace Obsidian.Net.Actions.BossBar
{
    public class BossBarUpdateStyleAction : BossBarAction
    {
        public override int Action => 4;
        public BossBarColor Color { get; set; }
        public BossBarDivisionType Division { get; set; }

        public override byte[] ToArray()
        {
            using var stream = new MinecraftStream();
            stream.WriteVarInt(Color);
            stream.WriteVarInt(Division);

            return stream.ToArray();
        }

        public override async Task<byte[]> ToArrayAsync()
        {
            using var stream = new MinecraftStream();
            await stream.WriteVarIntAsync(Color);
            await stream.WriteVarIntAsync(Division);

            return stream.ToArray();
        }
    }
}
