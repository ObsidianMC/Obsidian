using Obsidian.API;
using Obsidian.API.Registries;
using Obsidian.API.Inventory;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Registries;
using Painting = Obsidian.Entities.Painting;
using Xunit;

namespace Obsidian.Tests;

public class HangingEntities
{
    [Fact(DisplayName = "A hung item frame sits against the block it hangs on")]
    public void HangsItemFrames()
    {
        var frame = new ItemFrame { Level = null! };
        frame.Hang(new Vector(10, 64, -3), BlockFace.West);

        // Centered in its block, pushed 0.46875 toward the block east of it.
        Assert.Equal(new VectorD(10.96875, 64.5, -2.5), frame.Position);
        Assert.Equal(new Vector(10, 64, -3), frame.BlockPosition);
    }

    [Fact(DisplayName = "Item frames keep their item, rotation and block through saving")]
    public void SavesItemFrames()
    {
        var frame = new GlowItemFrame { Level = null!, Item = new ItemStack(ItemsRegistry.Elytra), Rotation = 5, Fixed = true };
        frame.Hang(new Vector(1, 2, 3), BlockFace.Up);

        var tag = new NbtCompound();
        frame.WriteNbt(tag);

        var loaded = new GlowItemFrame { Level = null! };
        loaded.ReadNbt(tag);

        Assert.Equal(new Vector(1, 2, 3), loaded.BlockPosition);
        Assert.Equal(BlockFace.Up, loaded.Facing);
        Assert.Equal(5, loaded.Rotation);
        Assert.True(loaded.Fixed);
        Assert.True(loaded.Item!.Is(ItemsRegistry.Elytra));
        Assert.Equal(frame.Position, loaded.Position);
    }

    [Fact(DisplayName = "Paintings of even size are centered between blocks")]
    public void CentersPaintings()
    {
        // Backyard is 3 wide and 4 high: centered on its block across, half a block up.
        var painting = new Painting { Level = null!, Variant = CodecRegistry.PaintingVariant.Backyard };
        painting.Hang(new Vector(0, 70, 0), BlockFace.South);

        Assert.Equal(new VectorD(0.5, 71, 0.03125), painting.Position);

        var box = painting.BoundingBox;
        Assert.Equal(3 - 0.125, box.Max.X - box.Min.X, 6);
        Assert.Equal(4 - 0.125, box.Max.Y - box.Min.Y, 6);
    }

    [Fact(DisplayName = "Paintings keep their variant through saving")]
    public void SavesPaintings()
    {
        var painting = new Painting { Level = null!, Variant = CodecRegistry.PaintingVariant.Baroque };
        painting.Hang(new Vector(4, 65, 4), BlockFace.East);

        var tag = new NbtCompound();
        painting.WriteNbt(tag);

        var loaded = new Painting { Level = null! };
        loaded.ReadNbt(tag);

        Assert.Equal("minecraft:baroque", loaded.Variant.Name);
        Assert.Equal(painting.Position, loaded.Position);
    }

    [Fact(DisplayName = "TNT lit by an explosion has a shorter, random fuse")]
    public void ShortensExplodedFuses()
    {
        for (var i = 0; i < 100; i++)
            Assert.InRange(PrimedTnt.ExplodedFuse(), PrimedTnt.DefaultFuse / 8, PrimedTnt.DefaultFuse / 8 + PrimedTnt.DefaultFuse / 4 - 1);

        var tnt = new PrimedTnt { Level = null!, Fuse = 37 };
        var tag = new NbtCompound();
        tnt.WriteNbt(tag);

        var loaded = new PrimedTnt { Level = null! };
        loaded.ReadNbt(tag);
        Assert.Equal(37, loaded.Fuse);
    }
}
