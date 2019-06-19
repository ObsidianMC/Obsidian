using Obsidian.Net;
using System.Threading.Tasks;

namespace Obsidian.BlockData
{
    public interface IBlockStatePalette
    {
        bool IsFull { get; }
        int IdFromState(BlockState blockState);
        BlockState StateFromIndex(int index);
        Task<byte[]> ToArrayAsync();
    }

    public class GlobalBlockStatePalette : IBlockStatePalette
    {
        public bool IsFull { get { return false; } }

        public int IdFromState(BlockState blockState)
        {
            return Blocks.BLOCK_STATES.IndexOf(blockState);
        }

        public BlockState StateFromIndex(int index)
        {
            return Blocks.BLOCK_STATES[index];
        }

        public Task<byte[]> ToArrayAsync()
        {
            return Task.FromResult(new byte[0]);
        }
    }

    public class LinearBlockStatePalette : IBlockStatePalette
    {
        public BlockState[] BlockStateArray;
        public int BlockStateCount = 0;

        public bool IsFull { get { return BlockStateArray.Length == BlockStateCount; } }

        public LinearBlockStatePalette(int bitCount)
        {
            this.BlockStateArray = new BlockState[1 << bitCount];
        }

        public int IdFromState(BlockState blockState)
        {
            for (int id = 0; id < BlockStateCount; id++)
            {
                if (BlockStateArray[id] == blockState)
                {
                    return id;
                }
            }

            if(this.IsFull)
            {
                return -1;
            }
            // Add to palette
            int newId = BlockStateCount;
            BlockStateArray[newId] = blockState;
            BlockStateCount++;
            return newId;
        }

        public BlockState StateFromIndex(int index)
        {
            return BlockStateArray[index];
        }

        public async Task<byte[]> ToArrayAsync()
        {
            using (var stream = new MinecraftStream())
            {
                await stream.WriteVarIntAsync(BlockStateCount);

                for(int i  = 0; i < BlockStateCount; i++)
                {
                    await stream.WriteVarIntAsync(BlockStateArray[i].Id);
                }

                return stream.ToArray();
            }
        }
    }
}
