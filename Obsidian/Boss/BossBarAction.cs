using System.Threading.Tasks;

namespace Obsidian.Boss
{
    public abstract class BossBarAction
    {
        public abstract int Action { get; }

        public abstract Task<byte[]> ToArrayAsync();
    }
}
