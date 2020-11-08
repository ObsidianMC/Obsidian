using Obsidian.Blocks;
using Obsidian.Net;
using Obsidian.Util.Registry;
using System.Linq;
using System.Threading.Tasks;

namespace Obsidian.ChunkData
{
    public class GlobalBlockStatePalette : IBlockStatePalette
    {
        public bool IsFull { get { return false; } }

        public int GetIdFromState(Block blockState) => Registry.Blocks.ToList().IndexOf(blockState);

        public Block GetStateFromIndex(int index) => Registry.Blocks.ToList()[index];

        public Task WriteToAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadFromAsync(MinecraftStream stream) => Task.CompletedTask;
    }
}
