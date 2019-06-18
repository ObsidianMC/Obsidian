using System.Threading.Tasks;

namespace Obsidian.BlockData
{
    public interface IBlockStatePalette
    {
        int IdFromState(BlockState blockState);
        BlockState StateFromIndex(int index);
        Task<byte[]> ToArrayAsync();
    }

    public class BlockStateGlobalPalette : IBlockStatePalette
    {
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
            return byte[0];
        }
    }
}
