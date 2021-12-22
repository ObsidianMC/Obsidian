using Obsidian.Net;
using Obsidian.Utilities.Registry;

namespace Obsidian.ChunkData;

public class GlobalBlockStatePalette : IPalette<Block>
{
    public int[] Values => throw new NotSupportedException();
    public int BitCount => 15;
    public int Count => throw new NotSupportedException();

    public bool IsFull => false;

    public bool TryGetId(Block block, out int id)
    {
        id = block.Id;
        return true;
    }

    public int GetOrAddId(Block block) => block.Id;

    public Block GetValueFromIndex(int index) => Registry.GetBlock(index);

    public Task WriteToAsync(MinecraftStream stream) => Task.CompletedTask;

    public Task ReadFromAsync(MinecraftStream stream) => Task.CompletedTask;
    public void WriteTo(MinecraftStream stream) { }
}
