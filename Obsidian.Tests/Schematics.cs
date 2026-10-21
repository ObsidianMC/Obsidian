using Obsidian.API;
using Obsidian.Commands.Modules;
using Obsidian.Nbt;
using System.Linq;
using Xunit;

namespace Obsidian.Tests;

public class Schematics
{
    [Fact]
    public void ReadsSpongeSchematic()
    {
        using var writer = new RawNbtWriter("");
        writer.WriteCompoundStart("Schematic");
        writer.WriteInt("Version", 3);
        writer.WriteShort("Width", 2);
        writer.WriteShort("Height", 1);
        writer.WriteShort("Length", 1);
        writer.WriteCompoundStart("Blocks");
        writer.WriteCompoundStart("Palette");
        writer.WriteInt("minecraft:air", 0);
        writer.WriteInt("minecraft:oak_stairs[facing=east,half=top]", 1);
        writer.EndCompound();
        writer.WriteArray("Data", new byte[] { 0, 1 });
        writer.EndCompound();
        writer.EndCompound();
        writer.EndCompound();
        writer.TryFinish();

        var schematic = SchematicCommandModule.Read(writer.Data.ToArray());

        Assert.Equal(new Vector(2, 1, 1), schematic.Size);
        var (offset, stairs) = schematic.Blocks.Single(entry => !entry.Block.IsAir);
        Assert.Equal(new Vector(1, 0, 0), offset);
        Assert.Equal("minecraft:oak_stairs", stairs.UnlocalizedName);
    }

    [Fact]
    public void ReadsStructure()
    {
        using var writer = new RawNbtWriter("");
        writer.WriteListStart("size", NbtTagType.Int, 3);
        writer.WriteInt(1);
        writer.WriteInt(2);
        writer.WriteInt(1);
        writer.EndList();
        writer.WriteListStart("palette", NbtTagType.Compound, 1);
        writer.WriteCompoundStart();
        writer.WriteString("Name", "minecraft:stone");
        writer.EndCompound();
        writer.EndList();
        writer.WriteListStart("blocks", NbtTagType.Compound, 1);
        writer.WriteCompoundStart();
        writer.WriteListStart("pos", NbtTagType.Int, 3);
        writer.WriteInt(0);
        writer.WriteInt(1);
        writer.WriteInt(0);
        writer.EndList();
        writer.WriteInt("state", 0);
        writer.EndCompound();
        writer.EndList();
        writer.EndCompound();
        writer.TryFinish();

        var schematic = SchematicCommandModule.Read(writer.Data.ToArray());

        var (offset, stone) = Assert.Single(schematic.Blocks);
        Assert.Equal(new Vector(0, 1, 0), offset);
        Assert.Equal("minecraft:stone", stone.UnlocalizedName);
    }
}
