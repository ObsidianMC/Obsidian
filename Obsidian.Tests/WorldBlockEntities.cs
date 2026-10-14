using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.BlockEntities;
using Obsidian.API.Containers;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Registries;
using Obsidian.API.Utilities;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Commands.Framework;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Payloads;
using Obsidian.Plugins;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Utilities;
using Obsidian.WorldData;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Block entities in the world: vanilla's saved as vanilla saves them, created and removed with their blocks, a
/// container's items dropping with it, and a plugin's ticked, saved, synced and kept across a reload.
/// </summary>
// Loading plugins merges their tags into vanilla's, which PluginTagFiles changes too.
[Collection(nameof(PluginTagFiles))]
public sealed class WorldBlockEntities : IDisposable
{
    private static readonly Vector origin = new(8, 70, 8);

    private readonly string folder = Path.Join(Path.GetTempPath(), $"obsidian-block-entities-{Guid.NewGuid():N}");

    public WorldBlockEntities() => Directory.CreateDirectory(this.folder);

    public void Dispose() => Directory.Delete(this.folder, recursive: true);

    [Fact(DisplayName = "Vanilla's block entities save what they load, the fields the server doesn't model included")]
    public void VanillaBlockEntitiesSaveWhatTheyLoad()
    {
        (string Id, Type Class, INbtTag[] Fields)[] rows =
        [
            ("minecraft:furnace", typeof(VanillaContainerBlockEntity),
            [
                Items(
                    Slotted(new ItemStack(ItemsRegistry.Stone, 3), 0),
                    Slotted(new ItemStack(ItemsRegistry.Apple, 5), 2)),
                ChatMessage.Simple("Smeltery").ToNbt("CustomName"),
                new NbtTag<short>("cooking_time_spent", 40),
                new NbtTag<short>("cooking_total_time", 200),
                new NbtTag<short>("lit_time_remaining", 900),
                new NbtTag<short>("lit_total_time", 1600),
                new NbtCompound("RecipesUsed") { new NbtTag<int>("minecraft:stone", 2) }
            ]),
            ("minecraft:chest", typeof(VanillaContainerBlockEntity),
            [
                new NbtTag<string>("LootTable", "minecraft:chests/simple_dungeon"),
                new NbtTag<long>("LootTableSeed", 42)
            ]),
            ("minecraft:brewing_stand", typeof(BrewingStandBlockEntity),
            [
                Items(Slotted(new ItemStack(ItemsRegistry.NetherWart, 2), BrewingStand.IngredientSlot)),
                new NbtTag<short>("BrewTime", 100),
                new NbtTag<byte>("Fuel", 5)
            ]),
            ("minecraft:hopper", typeof(VanillaContainerBlockEntity),
            [
                Items(Slotted(new ItemStack(ItemsRegistry.Diamond, 1), 4)),
                new NbtTag<int>("TransferCooldown", 3)
            ]),
            ("minecraft:mob_spawner", typeof(DataBlockEntity),
            [
                new NbtTag<short>("Delay", 20),
                new NbtCompound("SpawnData")
                {
                    new NbtCompound("entity") { new NbtTag<string>("id", "minecraft:zombie") }
                }
            ]),
            ("othermod:machine", typeof(DataBlockEntity), [new NbtTag<int>("energy", 5)])
        ];

        foreach (var (id, expectedClass, fields) in rows)
        {
            var tag = new NbtCompound
            {
                new NbtTag<string>("id", id),
                new NbtTag<int>("x", origin.X),
                new NbtTag<int>("y", origin.Y),
                new NbtTag<int>("z", origin.Z),
                new NbtTag<bool>("keepPacked", false)
            };

            foreach (var field in fields)
                tag.Add(field);

            var loaded = BlockEntityNbt.Load(tag, id, origin);

            Assert.IsType(expectedClass, loaded);
            AssertSameNbt(tag, BlockEntityNbt.Save(loaded)!);
        }

        // An unopened chest is a container, waiting for its loot.
        var chest = (ContainerBlockEntity)BlockEntityNbt.Create(
            "minecraft:chest",
            origin,
            new NbtCompound { new NbtTag<string>("LootTable", "minecraft:chests/simple_dungeon") });

        Assert.Equal("minecraft:chests/simple_dungeon", chest.LootTable);
        Assert.False(chest.Inventory.HasItems());
    }

    [Fact(DisplayName = "Vanilla's block entities come with their blocks: containers drop their items, stands brew",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task VanillaBlockEntitiesFollowTheirBlocks()
    {
        await using var context = TestServerFactory.CreateServer($"block-entities-{Guid.NewGuid():N}");
        var chunk = AddEmptyChunk(context.World);
        await JoinAsync(context);
        context.World.GrowthRandom = new LegacyRandomSource(7);

        var position = new Vector(4, 70, 4);
        await context.World.SetBlockAsync(position, BlocksRegistry.Chest);

        var chest = Assert.IsAssignableFrom<ContainerBlockEntity>(chunk.GetBlockEntity(4, 70, 4));
        chest.Inventory.SetItem(0, new ItemStack(ItemsRegistry.Stone, 64));
        chest.Inventory.SetItem(5, new ItemStack(ItemsRegistry.Apple, 3));

        // Facing another way, it's the same chest.
        await context.World.SetBlockAsync(position, BlocksRegistry.Chest.WithProperty("facing", "east"));
        Assert.Same(chest, chunk.GetBlockEntity(4, 70, 4));

        // Broken, it drops its items like vanilla's Containers.dropContents: stacks of 10 to 30 in the block's lower
        // part, tossed up.
        await context.World.SetBlockAsync(position, BlocksRegistry.Air);

        var drops = ItemsIn(context.World);
        Assert.Null(chunk.GetBlockEntity(4, 70, 4));
        Assert.True(chest.IsRemoved);
        Assert.False(chest.Inventory.HasItems());
        Assert.Equal(64, drops.Where(drop => drop.Item.Is(ItemsRegistry.Stone)).Sum(drop => drop.Item.Count));
        Assert.Equal(3, drops.Where(drop => drop.Item.Is(ItemsRegistry.Apple)).Sum(drop => drop.Item.Count));
        Assert.All(drops, drop =>
        {
            Assert.InRange(drop.Position.X, 4.125, 4.875);
            Assert.InRange(drop.Position.Y, 70, 70.75);
            Assert.InRange(drop.Position.Z, 4.125, 4.875);
            Assert.InRange(drop.Motion.Y, 0.2 - 0.11485, 0.2 + 0.11485);
        });
        Assert.All(drops.Where(drop => drop.Item.Is(ItemsRegistry.Stone)).SkipLast(1), drop =>
            Assert.InRange(drop.Item.Count, 10, 30));

        // A shulker box keeps its items, which vanilla's box item holds.
        await context.World.SetBlockAsync(position, BlocksRegistry.ShulkerBox);
        var box = Assert.IsAssignableFrom<ContainerBlockEntity>(chunk.GetBlockEntity(4, 70, 4));
        box.Inventory.SetItem(0, new ItemStack(ItemsRegistry.Diamond, 1));
        await context.World.SetBlockAsync(position, BlocksRegistry.Air);

        Assert.DoesNotContain(ItemsIn(context.World), drop => drop.Item.Is(ItemsRegistry.Diamond));

        // A brewing stand brews on the level's block entity tick, while its chunk ticks.
        await context.World.SetBlockAsync(position, BlocksRegistry.BrewingStand);
        var stand = Assert.IsType<BrewingStandBlockEntity>(chunk.GetBlockEntity(4, 70, 4)).Stand;
        stand.SetItem(0, new ItemStack(ItemsRegistry.Potion, 1, new PotionContentsDataComponent
        {
            Potion = Potion.Water
        }));
        stand.SetItem(BrewingStand.IngredientSlot, new ItemStack(ItemsRegistry.NetherWart, 1));
        stand.Fuel = 1;

        await context.World.DoWorldTickAsync();

        // The brew started: its 400 ticks count down from the next tick.
        Assert.Equal(400, stand.BrewTime);
    }

    [Fact(DisplayName = "A plugin's block entity ticks, saves and syncs, and comes back as it saved after a reload",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task PluginBlockEntityLifecycle()
    {
        await using var context = TestServerFactory.CreateServer($"plugin-block-entities-{Guid.NewGuid():N}");
        var chunk = AddEmptyChunk(context.World);
        var human = await JoinAsync(context);
        var client = (TestClient)human.Client;
        await using var manager = new PluginManager(
            context.Server,
            context.Provider,
            context.EventDispatcher,
            context.Provider.GetRequiredService<CommandHandler>(),
            NullLogger<PluginManager>.Instance,
            new ConfigurationBuilder().Build());
        var path = Path.Join(this.folder, "probe.obby");

        await File.WriteAllBytesAsync(path, PluginLoading.ProbeArchive("1.0.0"));
        var container = (await manager.LoadPluginAsync(path))!;

        Assert.True(BlockEntityTypesRegistry.TryGet("probe:counter", out var type));
        Assert.True(BlocksRegistry.TryGetPluginBlock("probe:counter", out var block));
        Assert.Equal([block], type.Blocks);

        // Synced to modded clients after vanilla's types, like the other registries plugins add to.
        var synced = SyncedRegistries.Get(SyncedRegistries.BlockEntityTypes);
        Assert.Contains((0, "minecraft:furnace"), synced.Entries);
        Assert.Contains((type.Id, "probe:counter"), synced.Entries);
        Assert.Contains(SyncedRegistries.GetModded(), registry => registry.Name == SyncedRegistries.BlockEntityTypes);

        // Setting its block creates it; it ticks while its chunk does, and sends players its updates.
        var position = new Vector(4, 70, 4);
        human.LoadedChunks.Add(NumericsHelper.IntsToLong(0, 0));
        await context.World.SetBlockAsync(position, block.DefaultState);

        var counter = Assert.IsAssignableFrom<BlockEntity>(chunk.GetBlockEntity(4, 70, 4));
        Assert.Equal(nameof(ProbeCounter), counter.GetType().Name);
        Assert.Same(context.World, counter.Level);

        client.ClearPackets();
        for (var tick = 0; tick < 3; tick++)
            await context.World.DoWorldTickAsync();

        var update = client.SentPackets.OfType<BlockEntityDataPacket>().Last();
        Assert.Equal(position, update.Position);
        Assert.Equal(type.Id, update.Type);
        Assert.Equal(3, update.NBTData.GetInt("ticks"));

        // Saved as of its last change, which the level took in at the end of the tick.
        var saved = BlockEntityNbt.Save(counter)!;
        Assert.Equal("probe:counter", saved.GetString("id"));
        Assert.Equal(3, saved.GetInt("ticks"));

        // Unloading the plugin keeps it in its chunk as it saved, and lets go of the plugin's object.
        await manager.UnloadPluginAsync(container);

        var kept = Assert.IsType<DataBlockEntity>(chunk.GetBlockEntity(4, 70, 4));
        Assert.True(counter.IsRemoved);
        Assert.Equal(3, kept.Data.GetInt("ticks"));
        Assert.Equal(3, BlockEntityNbt.Save(kept)!.GetInt("ticks"));

        await context.World.DoWorldTickAsync();
        Assert.Same(kept, chunk.GetBlockEntity(4, 70, 4));

        // Loaded again, the plugin gets it back from what it saved on the level's next tick, and it ticks on.
        await manager.LoadPluginAsync(path);
        await context.World.DoWorldTickAsync();

        var restored = Assert.IsAssignableFrom<BlockEntity>(chunk.GetBlockEntity(4, 70, 4));
        Assert.Equal(nameof(ProbeCounter), restored.GetType().Name);
        Assert.NotSame(counter.GetType(), restored.GetType());
        Assert.Equal(4, BlockEntityNbt.Save(restored)!.GetInt("ticks"));

        // Another block in its place removes it.
        await context.World.SetBlockAsync(position, BlocksRegistry.Stone);

        Assert.Null(chunk.GetBlockEntity(4, 70, 4));
        Assert.True(restored.IsRemoved);
    }

    private static NbtList Items(params NbtCompound[] items)
    {
        var list = new NbtList(NbtTagType.Compound, "Items");
        foreach (var item in items)
            list.Add(item);

        return list;
    }

    private static NbtCompound Slotted(ItemStack item, int slot)
    {
        var tag = item.ToNbt();
        tag.Add(new NbtTag<byte>("Slot", (byte)slot));

        return tag;
    }

    private static List<ItemEntity> ItemsIn(World world) =>
        [.. world.Regions.Values.SelectMany(region => region.Entities.Values).OfType<ItemEntity>()];

    // Compounds match whatever the order of their fields.
    private static void AssertSameNbt(INbtTag expected, INbtTag actual)
    {
        Assert.Equal(expected.Type, actual.Type);

        switch (expected)
        {
            case NbtCompound compound:
                var other = Assert.IsType<NbtCompound>(actual);
                Assert.Equal(compound.Select(field => field.Key).Order(), other.Select(field => field.Key).Order());

                foreach (var (name, child) in compound)
                    AssertSameNbt(child, other[name]);

                break;
            case NbtList list:
                var otherList = Assert.IsType<NbtList>(actual);
                Assert.Equal(list.Count, otherList.Count);

                for (var index = 0; index < list.Count; index++)
                    AssertSameNbt(list[index], otherList[index]);

                break;
            default:
                Assert.Equal(expected.ToString(), actual.ToString());
                break;
        }
    }

    private static async Task<Obsidian.Entities.Player> JoinAsync(RealServerContext context)
    {
        var human = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Human", (VectorD)origin);
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, human);

        return human;
    }

    // An empty plains chunk where the test's player stands, complete, so that it ticks.
    private static Chunk AddEmptyChunk(World world)
    {
        var chunk = new Chunk(0, 0);

        for (var x = 0; x < 16; x += 4)
        {
            for (var z = 0; z < 16; z += 4)
            {
                for (var y = -64; y < 320; y += 4)
                    chunk.SetBiome(x, y, z, CodecRegistry.Biomes.Plains);
            }
        }

        chunk.SetChunkStatus(ChunkGenStage.full);
        world.LoadRegionByChunk(0, 0).SetChunk(chunk);

        return chunk;
    }
}

/// <summary>
/// The probe plugin's block entity, which counts the ticks it was ticked, saves the count and sends it to players.
/// </summary>
public sealed class ProbeCounter : BlockEntity
{
    private int ticks;

    protected internal override void Load(NbtCompound tag) =>
        this.ticks = tag.TryGetTagValue<int>("ticks", out var ticks) ? ticks : 0;

    protected internal override void Save(NbtCompound tag) => tag.Add(new NbtTag<int>("ticks", this.ticks));

    protected internal override ValueTask TickAsync()
    {
        this.ticks++;
        this.SetChanged();
        this.SendUpdate();

        return ValueTask.CompletedTask;
    }

    protected internal override NbtCompound? GetUpdateData() => new() { new NbtTag<int>("ticks", this.ticks) };
}
