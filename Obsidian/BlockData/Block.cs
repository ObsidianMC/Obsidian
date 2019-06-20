using System.Collections.Generic;

namespace Obsidian.BlockData
{
    public class Blocks
    {
        public static List<BlockState> BLOCK_STATES { get; } = new List<BlockState>();

        public static BlockState Air => Add(new BlockState(0));
        public static BlockState Stone => Add(new BlockState(1));

        private static BlockState Add(BlockState blockState)
        {
            BLOCK_STATES.Add(blockState);
            return blockState;
        }
    }

    public class Block
    {
       
    }

    public class BlockState
    {
        public int Id = 0;

        public BlockState(int Id)
        {
            this.Id = Id;
        }
    }
}
