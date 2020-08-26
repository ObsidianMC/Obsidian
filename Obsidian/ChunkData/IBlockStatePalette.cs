using Obsidian.BlockData;
using Obsidian.Net;
using System.Threading.Tasks;

namespace Obsidian.ChunkData
{
    public interface IBlockStatePalette
    {
        bool IsFull { get; }
        int GetIdFromState(Block blockState);
        Block GetStateFromIndex(int index);
        Task WriteToAsync(MinecraftStream stream);
    }

}
