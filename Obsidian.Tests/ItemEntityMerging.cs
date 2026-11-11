using System;
using System.Linq;
using System.Threading.Tasks;
using Obsidian.API;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Registries;
using Obsidian.API.Utilities;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.WorldData;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Item entities lying together merge as vanilla 26.3's <c>ItemEntity.mergeWithNeighbours</c>, <c>isMergable</c>,
/// <c>areMergable</c> and <c>merge</c> say.
/// </summary>
public sealed class ItemEntityMerging
{
    private static readonly VectorD Centre = new(8.5, 70, 8.5);

    // An item looks for others to merge with every 40 ticks while it lies still; this covers two looks.
    private const int Ticks = 81;

    /// <summary>
    /// Vanilla's <c>merge</c> fills the larger stack only up to 64, even when its stacks go to 99: a stack of 65 that
    /// meets a stack of one is cut to 64, and the other grows to two (a transfer of -1, which vanilla makes too).
    /// </summary>
    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task StacksBeyondSixtyFourMergeAsVanillasDo()
    {
        await using var server = TestServerFactory.CreateServer($"item-merge-{Guid.NewGuid():N}");
        var world = await PrepareAsync(server);
        var larger = Drop(world, 0, Stack(65, maxStackSize: 99));
        var smaller = Drop(world, 1, Stack(1, maxStackSize: 99));

        await TickAsync(larger, smaller);

        Assert.Equal(64, larger.Item.Count);
        Assert.Equal(2, smaller.Item.Count);
        Assert.False(larger.Removed);
        Assert.False(smaller.Removed);
    }

    /// <summary>Stacks with room for each other merge into the larger one, which takes the other whole.</summary>
    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task StacksWithRoomMergeIntoTheLargerOne()
    {
        await using var server = TestServerFactory.CreateServer($"item-merge-{Guid.NewGuid():N}");
        var world = await PrepareAsync(server);
        var larger = Drop(world, 0, Stack(3));
        var smaller = Drop(world, 1, Stack(2));

        await TickAsync(larger, smaller);

        Assert.Equal(5, larger.Item.Count);
        Assert.True(smaller.Removed);
    }

    /// <summary>
    /// Vanilla's <c>isMergable</c> leaves out an item that can never be picked up (a pickup delay of 32767) and one
    /// with no lifetime (an age of -32768), and <c>tryToMerge</c> stacks with different owners; those stay as they are,
    /// as do the sentinels, which never run down.
    /// </summary>
    [Theory(Timeout = TestServerFactory.TestTimeout)]
    [InlineData("PickupDelay")]
    [InlineData("Age")]
    [InlineData("Owner")]
    public async Task ItemsVanillaWontMergeStayApart(string field)
    {
        await using var server = TestServerFactory.CreateServer($"item-merge-{Guid.NewGuid():N}");
        var world = await PrepareAsync(server);
        var kept = Drop(world, 0, Stack(3));
        var other = Drop(world, 1, Stack(2));
        var saved = new NbtCompound();
        kept.WriteNbt(saved);
        switch (field)
        {
            case "PickupDelay":
                saved.Set(new NbtTag<short>("PickupDelay", short.MaxValue));
                break;
            case "Age":
                saved.Set(new NbtTag<short>("Age", short.MinValue));
                break;
            default:
                saved.Set(new NbtArray<int>("Owner", EntityNbt.UuidToInts(Guid.NewGuid())));
                break;
        }

        kept.ReadNbt(saved);

        await TickAsync(kept, other);

        Assert.Equal(3, kept.Item.Count);
        Assert.Equal(2, other.Item.Count);
        Assert.False(kept.Removed);
        Assert.False(other.Removed);

        var after = new NbtCompound();
        kept.WriteNbt(after);
        switch (field)
        {
            case "PickupDelay":
                Assert.True(after.TryGetNumber<short>("PickupDelay", out var delay));
                Assert.Equal(short.MaxValue, delay);
                break;
            case "Age":
                Assert.True(after.TryGetNumber<short>("Age", out var age));
                Assert.Equal(short.MinValue, age);
                break;
            default:
                Assert.True(after.TryGetTag<NbtArray<int>>("Owner", out var owner));
                Assert.True(saved.TryGetTag<NbtArray<int>>("Owner", out var expected));
                Assert.Equal(expected.GetArray(), owner.GetArray());
                break;
        }
    }

    private static ItemStack Stack(int count, int? maxStackSize = null) =>
        maxStackSize is int size
            ? new ItemStack(ItemsRegistry.Diamond, count, ComponentBuilder.MaxStackSize with { Value = size })
            : new ItemStack(ItemsRegistry.Diamond, count);

    private static async Task<World> PrepareAsync(RealServerContext context)
    {
        var world = context.World;
        WorldBlockEntities.AddEmptyChunk(world);
        for (var x = 0; x < 16; x++)
        {
            for (var z = 0; z < 16; z++)
                await world.SetBlockAsync(new Vector(x, 69, z), BlocksRegistry.Stone);
        }

        return world;
    }

    private static ItemEntity Drop(AbstractLevel world, int index, ItemStack stack)
    {
        var item = new ItemEntity
        {
            Level = world,
            EntityId = 710_000 + index,
            Position = Centre,
            Item = stack
        };
        Assert.True(world.TryAddEntity(item));
        return item;
    }

    private static async Task TickAsync(params ItemEntity[] items)
    {
        for (var tick = 0; tick < Ticks; tick++)
        {
            foreach (var item in items.Where(item => !item.Removed))
                await item.TickAsync();
        }
    }
}
