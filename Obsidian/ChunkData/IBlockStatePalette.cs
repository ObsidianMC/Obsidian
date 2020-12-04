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

        Task ReadFromAsync(MinecraftStream stream);
    }


    public static class Palette
    {
        public static IBlockStatePalette DeterminePalette(this byte bitsPerBlock)
        {
            if (bitsPerBlock <= 4)
                return new LinearBlockStatePalette(4);
            else if (bitsPerBlock > 4 || bitsPerBlock <= 8)
                return new LinearBlockStatePalette(bitsPerBlock);

            return new GlobalBlockStatePalette();
        }
    }
}
