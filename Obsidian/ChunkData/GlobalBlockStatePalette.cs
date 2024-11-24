using Obsidian.Net;
using Obsidian.Registries;

namespace Obsidian.ChunkData;

public class GlobalBlockStatePalette : IPalette<IBlock>
{
    public int[] Values => throw new NotSupportedException();
    public int BitCount { get; }
    public int Count => throw new NotSupportedException();

    public bool IsFull => false;

    public GlobalBlockStatePalette(int bitCount)
    {
        this.BitCount = bitCount;
    }

    public bool TryGetId(IBlock block, out int id)
    {
        id = block.GetHashCode();
        return true;
    }

    public int GetOrAddId(IBlock block) => block.GetHashCode();

    public IBlock GetValueFromIndex(int index) => BlocksRegistry.Get(index);

    public IPalette<IBlock> Clone() => this;

    public void WriteTo(INetStreamWriter writer) { }
}
