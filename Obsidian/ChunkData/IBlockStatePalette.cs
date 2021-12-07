using Obsidian.Net;

namespace Obsidian.ChunkData;

public interface IBlockStatePalette : IPalette
{
    int GetIdFromState(Block blockState);
    Block GetStateFromIndex(int index);
}
