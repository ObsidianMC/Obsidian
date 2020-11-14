using Obsidian.Net;
using Obsidian.Util.Registry;
using System.Threading.Tasks;

namespace Obsidian.ChunkData
{
    public class GlobalBlockStatePalette : IBlockStatePalette
    {
        public bool IsFull { get { return false; } }

        public int GetIdFromState(SebastiansBlock block) => block.Id;

        public SebastiansBlock GetStateFromIndex(int index) => Registry.GetBlock(index);

        public Task WriteToAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadFromAsync(MinecraftStream stream) => Task.CompletedTask;
    }
}
