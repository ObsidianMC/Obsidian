using System.Threading.Tasks;

namespace Obsidian.Net.Actions.BossBar
{
    public abstract class BossBarAction
    {
        public abstract int Action { get; }

        public abstract byte[] ToArray();

        public abstract Task<byte[]> ToArrayAsync();
    }
}
