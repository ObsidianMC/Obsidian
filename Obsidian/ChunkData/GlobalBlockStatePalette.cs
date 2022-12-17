using Obsidian.Net;
using Obsidian.Utilities.Registry;

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
        id = block.State.Id;
        return true;
    }

    public int GetOrAddId(IBlock block) => block.State.Id;

    public IBlock GetValueFromIndex(int index) => BlocksRegistry.Get(index);

    public IPalette<IBlock> Clone() => this;

    public Task WriteToAsync(MinecraftStream stream) => Task.CompletedTask;

    public Task ReadFromAsync(MinecraftStream stream) => Task.CompletedTask;
    public void WriteTo(MinecraftStream stream) { }
}
