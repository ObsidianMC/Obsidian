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

        public byte BitsPerBlock { get => 14; }

        public int GetIdFromState(Block blockState)
        {
            return BlockRegistry.BLOCK_STATES.Values.ToList().IndexOf(blockState);
        }

        public BlockState GetStateFromIndex(int index)
        {
            return BlockRegistry.BLOCK_STATES.Values.ToList()[index];
        }

        public Task WriteToAsync(MinecraftStream stream)
        {
            return Task.CompletedTask;
        }
    }

    public class LinearBlockStatePalette : IBlockStatePalette
    {
        public BlockState[] BlockStateArray;
        public int BlockStateCount;

        public bool IsFull { get { return BlockStateArray.Length == BlockStateCount; } }

        public LinearBlockStatePalette(byte bitCount)
        {
            this.BlockStateArray = new BlockState[1 << bitCount];
        }

        public int GetIdFromState(Block blockState)
        {
            for (int id = 0; id < this.BlockStateCount; id++)
            {
                if (this.BlockStateArray[id] == blockState)
                    return id;
            }

            if (this.IsFull)
                return -1;
            
            // Add to palette
            int newId = this.BlockStateCount;
            this.BlockStateArray[newId] = blockState;
            this.BlockStateCount++;
            return newId;
        }

        public BlockState GetStateFromIndex(int index) => BlockStateArray[index];

        public async Task WriteToAsync(MinecraftStream stream)
        {
            await stream.WriteVarIntAsync(this.BlockStateCount);

            for (int i = 0; i < this.BlockStateCount; i++)
                await stream.WriteVarIntAsync(this.BlockStateArray[i].Id);
        }
    }
}
