using System.Threading.Tasks;

namespace Obsidian.PlayerInfo
{
    public abstract class PlayerInfoAction
    {
        public abstract Task<byte[]> ToArrayAsync();
    }
}
