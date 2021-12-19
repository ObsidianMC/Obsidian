using Obsidian.Net;
using Obsidian.Utilities.Collection;

namespace Obsidian.ChunkData;

public sealed class BlockStateContainer : IDataContainer<Block>
{
    public byte BitsPerEntry { get; }

    public DataArray DataArray { get; }

    public IPalette<Block> Palette { get; internal set; }

    public bool IsEmpty => this.DataArray.storage.Length <= 0;

    internal BlockStateContainer(byte bitsPerEntry = 4)
    {
        this.BitsPerEntry = bitsPerEntry;

        this.DataArray = new DataArray(bitsPerEntry, 4096);

        this.Palette = bitsPerEntry.DetermineBlockPalette();
    }

    public bool Set(int x, int y, int z, Block blockState)
    {
        var blockIndex = GetIndex(x, y, z);

        int paletteIndex = this.Palette.GetIdFromValue(blockState);
        if (paletteIndex == -1) { return false; }

        this.DataArray[blockIndex] = paletteIndex;
        return true;
    }

    public Block Get(int x, int y, int z)
    {
        int storageId = this.DataArray[GetIndex(x, y, z)];

        return this.Palette.GetValueFromIndex(storageId);
    }

    public int GetIndex(int x, int y, int z) => ((y * 16) + z) * 16 + x;

    public async Task WriteToAsync(MinecraftStream stream)
    {
        var validBlocks = this.GetNonAirBlocks();

        await stream.WriteShortAsync(validBlocks);
        await stream.WriteUnsignedByteAsync(this.BitsPerEntry);

        await this.Palette.WriteToAsync(stream);

        await stream.WriteVarIntAsync(this.DataArray.storage.Length);
        await stream.WriteLongArrayAsync(this.DataArray.storage);
    }

    public void WriteTo(MinecraftStream stream)
    {
        var validBlocks = this.GetNonAirBlocks();

        stream.WriteShort(validBlocks);
        stream.WriteUnsignedByte(BitsPerEntry);

        Palette.WriteTo(stream);

        stream.WriteVarInt(DataArray.storage.Length);

        long[] storage = DataArray.storage;
        for (int i = 0; i < storage.Length; i++)
            stream.WriteLong(storage[i]);
    }

    private short GetNonAirBlocks()
    {
        short validBlockCount = 0;
        for (int x = 0; x < 16; x++)
        {
            for (int y = 0; y < 16; y++)
            {
                for (int z = 0; z < 16; z++)
                {
                    var block = this.Get(x, y, z);

                    if (!block.IsAir)
                        validBlockCount++;
                }
            }
        }

        return validBlockCount;
    }
}
