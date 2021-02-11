using System.Threading.Tasks;

namespace Obsidian.Net.Actions.BossBar
{
    public class BossBarUpdateHealthAction : BossBarAction
    {
        public override int Action => 2;
        public float Health { get; set; }

        public override byte[] ToArray()
        {
            using var stream = new MinecraftStream();
            stream.WriteFloat(Health);

            return stream.ToArray();
        }

        public override async Task<byte[]> ToArrayAsync()
        {
            using var stream = new MinecraftStream();
            await stream.WriteFloatAsync(Health);

            return stream.ToArray();
        }
    }
}
