using System.Threading.Tasks;

namespace Obsidian.PlayerData.Info
{
    public abstract class PlayerInfoAction
    {
        public abstract Task<byte[]> ToArrayAsync();
    }
}
