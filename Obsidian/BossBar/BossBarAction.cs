using System.Threading.Tasks;

namespace Obsidian.BossBar
{
    public abstract class BossBarAction
    {
        public abstract int Action { get; }

        public abstract Task<byte[]> ToArrayAsync();
    }
}
