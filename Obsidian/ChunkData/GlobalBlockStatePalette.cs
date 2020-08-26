using Obsidian.BlockData;
using Obsidian.Net;
using Obsidian.Util.Registry;
using System.Linq;
using System.Threading.Tasks;

namespace Obsidian.ChunkData
{
    public class GlobalBlockStatePalette : IBlockStatePalette
    {
        public bool IsFull { get { return false; } }

        public int GetIdFromState(Block blockState) => BlockRegistry.BLOCK_STATES.Values.ToList().IndexOf(blockState);

        public Block GetStateFromIndex(int index) => BlockRegistry.BLOCK_STATES.Values.ToList()[index];

        public Task WriteToAsync(MinecraftStream stream) => Task.CompletedTask;
    }
}
