using Obsidian.Net;

using System.IO;
using System.Threading.Tasks;

namespace Obsidian.Boss
{
    public class BossBarUpdateStyleAction : BossBarAction
    {
        public override int Action => 4;
        public BossBarColor Color { get; set; }
        public BossBarDivisionType Division { get; set; }

        public override async Task<byte[]> ToArrayAsync()
        {
            using (var stream = new MinecraftStream())
            {
                await stream.WriteVarIntAsync(Color);
                await stream.WriteVarIntAsync(Division);

                return stream.ToArray();
            }
        }
    }
}
