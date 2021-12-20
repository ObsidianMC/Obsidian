using Obsidian.Net;
using Obsidian.Utilities.Registry;

namespace Obsidian.ChunkData;

public class GlobalBlockStatePalette : IPalette<Block>
{
    public int[] Values => throw new NotImplementedException();

    public int Count => throw new NotImplementedException();

    public bool IsFull => false;

    public int GetIdFromValue(Block block) => block.Id;

    public Block GetValueFromIndex(int index) => Registry.GetBlock(index);

    public Task WriteToAsync(MinecraftStream stream) => Task.CompletedTask;

    public Task ReadFromAsync(MinecraftStream stream) => Task.CompletedTask;
    public void WriteTo(MinecraftStream stream) { }
}
