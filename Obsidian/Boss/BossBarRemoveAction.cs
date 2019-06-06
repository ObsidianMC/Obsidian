using System.Threading.Tasks;

namespace Obsidian.Boss
{
    public class BossBarRemoveAction : BossBarAction
    {
        public override int Action => 1;

        public override async Task<byte[]> ToArrayAsync() => new byte[0];
    }
}
