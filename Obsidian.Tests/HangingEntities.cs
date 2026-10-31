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
}
