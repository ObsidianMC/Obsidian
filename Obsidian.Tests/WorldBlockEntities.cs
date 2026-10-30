using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.BlockEntities;
using Obsidian.API.Blocks;
using Obsidian.API.Containers;
using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Loot;
using Obsidian.API.Loot.Entries;
using Obsidian.API.Loot.Functions;
using Obsidian.API.Loot.Numbers;
using Obsidian.API.Registries;
using Obsidian.API.Utilities;
using Obsidian.API.World;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Commands.Framework;
using Obsidian.Entities;
using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Packets.Play.Serverbound;
using Obsidian.Net;
using Obsidian.Plugins;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Utilities;
using Obsidian.WorldData;
using Obsidian.WorldData.Features;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Block entities in the world: vanilla's saved as vanilla saves them, created and removed with their blocks, a
/// container's items dropping with it, and a plugin's kept across a reload (SamplePlugin's ticks, saves and syncs).
/// </summary>
// Loading plugins merges their tags into vanilla's, which PluginTagFiles changes too.
[Collection(nameof(PluginTagFiles))]
public sealed class WorldBlockEntities : IDisposable
{
    private static readonly Vector origin = new(8, 70, 8);

    private readonly string folder = Path.Join(Path.GetTempPath(), $"obsidian-block-entities-{Guid.NewGuid():N}");

    public WorldBlockEntities() => Directory.CreateDirectory(this.folder);

    public void Dispose() => Directory.Delete(this.folder, recursive: true);

    [Fact]
    public async Task MultipartShapesSelectAndBreakOnlyTheNearestPart()
    {
        await using var context = TestServerFactory.CreateServer($"multipart-{Guid.NewGuid():N}");
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
        var plugin = (await manager.LoadPluginAsync(path))!;

        Assert.True(BlocksRegistry.TryGetPluginBlock("probe:parts", out var block));
        var position = new Vector(4, 70, 4);
        human.LoadedChunks.Add(NumericsHelper.IntsToLong(0, 0));
        client.ClearPackets();
        await context.World.SetBlockAsync(position, block.DefaultState);
        await context.World.DoWorldTickAsync();
        var placedPackets = client.SentPackets.ToList();
        var initial = Assert.Single(placedPackets.OfType<BlockEntityDataPacket>());
        Assert.True(initial.NBTData.GetBool("front"));
        Assert.IsType<BlockUpdatePacket>(placedPackets[placedPackets.IndexOf(initial) - 1]);
        var terrain = new MobTerrain(context.World);
        var eye = new VectorD(4.5, 70.5, 2);
        var end = new VectorD(4.5, 70.5, 7);
        var hit = terrain.RaycastOutlines(eye, end)!.Value;
        Assert.Equal(position, hit.Position);
        Assert.Equal(1, hit.BoxIndex);
        Assert.Equal(BlockFace.North, hit.Face);
        Assert.Equal(new VectorD(0.5, 0.5, 0.125), hit.ClickedAt);
        Assert.False(terrain.IsFree(new BoundingBox(new VectorD(4.4, 70.4, 4.2), new VectorD(4.6, 70.6, 4.3))));
        Assert.True(terrain.IsFree(new BoundingBox(new VectorD(4.05, 70.4, 4.2), new VectorD(4.2, 70.6, 4.3))));

        // Published multipart collision takes precedence over the block state's full-cube template.
        Assert.False(context.World.IsFaceSturdy(position, BlockFace.Up));
        Assert.False(context.World.IsFaceSturdy(position, BlockFace.North, SupportType.Center));
        Assert.All(Enum.GetValues<SupportType>(), mode =>
            Assert.False(context.World.BlockUpdates.IsFaceSturdy(position, BlockFace.Up, mode)));

        // The use packet supplies its own local hit coordinates.
        using var useData = new NetworkBuffer();
        useData.WriteVarInt((int)InteractionHand.MainHand);
        useData.WritePosition(position);
        useData.WriteVarInt((int)BlockFace.North);
        useData.WriteAbsoluteFloatPositionF(new VectorF(0.5f, 0.5f, 0.125f));
        useData.WriteBoolean(false);
        useData.WriteBoolean(false);
        useData.WriteVarInt(1);
        using var useReader = new NetworkBuffer(useData.AsSpan(0, useData.Size).ToArray());
        var use = new UseItemOnPacket();
        use.Populate(useReader);
        await use.HandleAsync(context.Server, human);
        Assert.Equal(hit.ClickedAt, AppContext.GetData(ProbePartsBehavior.UseKey));

        human.GameMode = GameMode.Creative;
        human.Pose = Pose.Sneaking;
        Assert.True(human.TryUpdateAttribute("minecraft:scale", 0.5f)
            || human.TryAddAttribute("minecraft:scale", 0.5f));
        human.Position = new VectorD(4.5, eye.Y - 1.27f * 0.5f, 2);
        human.Yaw = 0;
        human.Pitch = 0;

        using var breakData = new NetworkBuffer();
        breakData.WriteVarInt((int)PlayerActionStatus.StartedDigging);
        breakData.WritePosition(position);
        breakData.WriteByte((byte)BlockFace.South); // The server derives the true face, not this packet's claim.
        breakData.WriteVarInt(2);
        using var breakReader = new NetworkBuffer(breakData.AsSpan(0, breakData.Size).ToArray());
        var breaking = new PlayerActionPacket();
        breaking.Populate(breakReader);

        // An intervening outline blocks the target; the multipart hook must not run.
        AppContext.SetData(ProbePartsBehavior.BreakKey, null);
        await context.World.SetBlockAsync(new Vector(4, 70, 3), BlocksRegistry.Stone);
        await breaking.HandleAsync(context.Server, human);
        await context.World.DoWorldTickAsync();
        Assert.Null(AppContext.GetData(ProbePartsBehavior.BreakKey));
        await context.World.SetBlockAsync(new Vector(4, 70, 3), BlocksRegistry.Air);

        client.ClearPackets();
        await breaking.HandleAsync(context.Server, human);
        Assert.Null(AppContext.GetData(ProbePartsBehavior.BreakKey));
        await context.World.DoWorldTickAsync();
        Assert.Equal(hit, AppContext.GetData(ProbePartsBehavior.BreakKey));
        Assert.Equal(block.DefaultState.GetHashCode(),
            chunk.GetBlock(position.X, position.Y, position.Z).GetHashCode());
        Assert.False(BlockEntityNbt.Save(chunk.GetBlockEntity(4, 70, 4)!)!.GetBool("front"));
        var correctedPackets = client.SentPackets.ToList();
        var correction = correctedPackets.OfType<BlockEntityDataPacket>().First();
        Assert.False(correction.NBTData.GetBool("front"));
        Assert.IsType<BlockUpdatePacket>(correctedPackets[correctedPackets.IndexOf(correction) - 1]);
        Assert.Equal(0.625, terrain.RaycastOutlines(eye, end)!.Value.ClickedAt.Z);
        Assert.True(terrain.IsFree(new BoundingBox(new VectorD(4.4, 70.4, 4.2), new VectorD(4.6, 70.6, 4.3))));

        Assert.DoesNotContain(ItemsIn(context.World), item => item.Item.Is(ItemsRegistry.Diamond));

        // Unloading releases shape behaviour while keeping the parts' data; reloading restores the remaining part.
        await manager.UnloadPluginAsync(plugin);
        Assert.Null(BlockEntityShapes.Get(context.World, position, block.DefaultState));
        Assert.IsType<DataBlockEntity>(chunk.GetBlockEntity(4, 70, 4));
        Assert.True(context.World.IsFaceSturdy(position, BlockFace.Up));
        await manager.LoadPluginAsync(path);
        client.ClearPackets();
        await context.World.DoWorldTickAsync();
        Assert.Equal(0.625, terrain.RaycastOutlines(eye, end)!.Value.ClickedAt.Z);
        Assert.False(Assert.Single(client.SentPackets.OfType<BlockEntityDataPacket>()).NBTData.GetBool("front"));
        Assert.False(context.World.IsFaceSturdy(position, BlockFace.Up));

        // The final part permits ordinary block removal.
        human.Pose = Pose.Swimming;
        human.Position = new VectorD(4.5, eye.Y - 0.4f * 0.5f, 2);
        await breaking.HandleAsync(context.Server, human);
        await context.World.DoWorldTickAsync();
        Assert.True(chunk.GetBlock(position.X, position.Y, position.Z).IsAir);
        Assert.Null(chunk.GetBlockEntity(4, 70, 4));

        // Both attempts run in one tick: the second must select index zero of the remaining outline list.
        await context.World.SetBlockAsync(position, block.DefaultState);
        await breaking.HandleAsync(context.Server, human);
        await breaking.HandleAsync(context.Server, human);
        await context.World.DoWorldTickAsync();
        var lastHit = Assert.IsType<BlockShapeHit>(AppContext.GetData(ProbePartsBehavior.BreakKey));
        Assert.Equal(0, lastHit.BoxIndex);
        Assert.Equal(0.625, lastHit.ClickedAt.Z);
        Assert.True(chunk.GetBlock(position.X, position.Y, position.Z).IsAir);

        // A block entity removed from elsewhere, here the test's thread, runs OnRemoved on the level's next tick, in
        // turn with its other callbacks. Its plugin waits for that before it's released.
        await context.World.DoWorldTickAsync();
        await context.World.SetBlockAsync(position, block.DefaultState);
        AppContext.SetData(ProbeParts.RemovedKey, null);
        await context.World.SetBlockAsync(position, BlocksRegistry.Stone);

        Assert.Null(AppContext.GetData(ProbeParts.RemovedKey));
        await context.World.DoWorldTickAsync();
        Assert.Equal(true, AppContext.GetData(ProbeParts.RemovedKey));

        // Survival selective removal drops the part even though cancellation keeps its host block.
        human.GameMode = GameMode.Survival;
        await context.World.SetBlockAsync(position, block.DefaultState);
        await context.World.DoWorldTickAsync();
        await context.EventDispatcher.ExecuteEventAsync(new BlockBreakEventArgs(
            context.Server, human, block.DefaultState, position, context.World));
        await context.World.DoWorldTickAsync();
        Assert.False(chunk.GetBlock(position.X, position.Y, position.Z).IsAir);
        var partDrop = Assert.Single(ItemsIn(context.World), item => item.Item.Is(ItemsRegistry.Diamond));
        Assert.Equal(2, partDrop.Item.Count);

        var active = Assert.Single(manager.Plugins);
        await manager.UnloadPluginAsync(active);
        Assert.Contains(partDrop, ItemsIn(context.World));
        await context.World.SetBlockAsync(position, BlocksRegistry.Air);
        await manager.LoadPluginAsync(path);

        // The probe omits its item normally. Loan a vanilla carrier to exercise collection without another type.
        block.Item = ItemsRegistry.Stone;
        try
        {
            // Losing support takes the drop-aware path, collecting plugin data on the serial tick queue.
            var below = position + Vector.Down;
            await context.World.SetBlockAsync(below, BlocksRegistry.Stone);
            await context.World.SetBlockAsync(position, block.DefaultState);
            await context.World.DoWorldTickAsync();
            AppContext.SetData(ProbeParts.CollectedKey, null);
            Assert.True(context.World.DestroyBlock(below, dropItems: false));
            await context.World.DoWorldTickAsync();
            await context.World.DoWorldTickAsync();
            Assert.True(chunk.GetBlock(position.X, position.Y, position.Z).IsAir);
            Assert.Equal(true, AppContext.GetData(ProbeParts.CollectedKey));
            Assert.Single(ItemsIn(context.World), item => item.Item.Is(ItemsRegistry.Stone));
            await context.World.DoWorldTickAsync();

            foreach (var item in ItemsIn(context.World).Where(item => item.Item.Is(ItemsRegistry.Stone)))
                await item.RemoveAsync();

            // A plugin returning air shares the shape update's suppression flag, rather than queuing its own drops.
            await context.World.SetBlockAsync(position, block.DefaultState);
            context.World.BlockUpdates.ShapeUpdateAt(position, BlockFace.Down,
                BlockUpdateFlags.Clients | BlockUpdateFlags.SuppressDrops, 511);
            await context.World.DoWorldTickAsync();
            Assert.True(chunk.GetBlock(position.X, position.Y, position.Z).IsAir);
            Assert.DoesNotContain(ItemsIn(context.World), item => item.Item.Is(ItemsRegistry.Stone));
        }
        finally
        {
            block.Item = null;
        }
    }

    [Fact]
    public async Task DestroyBlockDropsLootAndContentsAndRejectsReplacedTargets()
    {
        await using var context = TestServerFactory.CreateServer($"destroy-block-{Guid.NewGuid():N}");
        var chunk = AddEmptyChunk(context.World);
        var position = new Vector(4, 70, 4);

        // Drop-aware destruction shares container removal: ordinary contents spill and shulkers keep theirs.
        await context.World.SetBlockAsync(position, BlocksRegistry.Chest);
        var chest = Assert.IsAssignableFrom<ContainerBlockEntity>(chunk.GetBlockEntity(4, 70, 4));
        chest.Inventory.SetItem(0, new ItemStack(ItemsRegistry.Diamond, 3));
        Assert.True(context.World.BlockUpdates.DestroyBlock(position, dropItems: true));
        Assert.False(context.World.BlockUpdates.DestroyBlock(position, dropItems: true));
        Assert.False(chunk.GetBlock(position.X, position.Y, position.Z).IsAir);
        await context.World.DoWorldTickAsync();
        Assert.True(chunk.GetBlock(position.X, position.Y, position.Z).IsAir);
        Assert.Equal(3, ItemsIn(context.World).Where(item => item.Item.Is(ItemsRegistry.Diamond))
            .Sum(item => item.Item.Count));
        Assert.Single(ItemsIn(context.World), item => item.Item.Is(ItemsRegistry.Chest));

        await context.World.SetBlockAsync(position, BlocksRegistry.ShulkerBox);
        var box = Assert.IsAssignableFrom<ContainerBlockEntity>(chunk.GetBlockEntity(4, 70, 4));
        box.Inventory.SetItem(0, new ItemStack(ItemsRegistry.Emerald, 4));
        Assert.True(context.World.DestroyBlock(position, dropItems: true));
        await context.World.DoWorldTickAsync();
        var boxItem = Assert.Single(ItemsIn(context.World), item => item.Item.Is(ItemsRegistry.ShulkerBox)).Item;
        Assert.Equal(4, boxItem.GetComponent<SimpleDataComponent<ImmutableArray<ItemStack>>>(
            DataComponentType.Container)!.Value[0].Count);
        Assert.DoesNotContain(ItemsIn(context.World), item => item.Item.Is(ItemsRegistry.Emerald));

        var waterlogged = BlocksRegistry.OakFence.WithProperty("waterlogged", "true");
        await context.World.SetBlockAsync(position, waterlogged);
        Assert.True(context.World.DestroyBlock(position, dropItems: false));
        await context.World.DoWorldTickAsync();
        Assert.True(chunk.GetBlock(position.X, position.Y, position.Z).Is(BlockIds.Water));
        Assert.DoesNotContain(ItemsIn(context.World), item => item.Item.Is(ItemsRegistry.OakFence));

        await context.World.SetBlockAsync(position, waterlogged);
        Assert.True(context.World.DestroyBlock(position, dropItems: false));
        await context.World.SetBlockAsync(position, BlocksRegistry.Stone);
        await context.World.DoWorldTickAsync();
        Assert.True(chunk.GetBlock(position.X, position.Y, position.Z).Is(BlockIds.Stone));
    }

    [Fact(DisplayName = "Vanilla's block entities save what they load, the fields the server doesn't model included")]
    public void VanillaBlockEntitiesSaveWhatTheyLoad()
    {
        (string Id, Type Class, INbtTag[] Fields)[] rows =
        [
            ("minecraft:furnace", typeof(FurnaceBlockEntity),
            [
                Items(
                    Slotted(new ItemStack(ItemsRegistry.Stone, 3), 0),
                    Slotted(new ItemStack(ItemsRegistry.Apple, 5), 2)),
                ChatMessage.Simple("Smeltery").ToNbt("CustomName"),
                new NbtTag<int>("cooking_time_spent", 40),
                new NbtTag<int>("cooking_total_time", 200),
                new NbtTag<int>("lit_time_remaining", 90000),
                new NbtTag<int>("lit_total_time", 160000),
                new NbtTag<float>("speed_multiplier", 2.5f),
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
                new NbtTag<int>("BrewTime", 50000),
                new NbtTag<int>("Fuel", 300),
                new NbtTag<int>("total_brew_time", 55000),
                new NbtTag<int>("total_fuel", 400),
                new NbtTag<float>("speed_multiplier", 0.5f)
            ]),
            ("minecraft:hopper", typeof(HopperBlockEntity),
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
            ("minecraft:sign", typeof(DataBlockEntity),
            [
                new NbtTag<byte>("allow_op_features", 1),
                new NbtTag<byte>("is_waxed", 1)
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

            BlockEntityNbt.EnsureSaveDefaults(tag);
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

    [Fact(DisplayName = "A plugin's container stops taking clicks as its plugin unloads, and comes back with its items",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task PluginContainersSurviveReloadsOnce()
    {
        await using var context = TestServerFactory.CreateServer($"plugin-crates-{Guid.NewGuid():N}");
        var chunk = AddEmptyChunk(context.World);
        var human = await JoinAsync(context);
        await using var manager = new PluginManager(
            context.Server,
            context.Provider,
            context.EventDispatcher,
            context.Provider.GetRequiredService<CommandHandler>(),
            NullLogger<PluginManager>.Instance,
            new ConfigurationBuilder().Build());
        var path = Path.Join(this.folder, "probe.obby");
        var keys = new[]
        {
            ProbeCrate.LoadsKey,
            ProbeCrate.UnloadedKey,
            ProbeCrate.TicksKey,
            ProbeCrate.ItemsChangedKey
        };
        foreach (var key in keys)
            AppContext.SetData(key, null);

        await File.WriteAllBytesAsync(path, PluginLoading.ProbeArchive("1.0.0"));
        var container = (await manager.LoadPluginAsync(path))!;
        Assert.True(BlocksRegistry.TryGetPluginBlock("probe:crate", out var crateBlock));

        var position = new Vector(4, 70, 4);
        await context.World.SetBlockAsync(position, crateBlock.DefaultState);

        var crate = Assert.IsAssignableFrom<ContainerBlockEntity>(chunk.GetBlockEntity(4, 70, 4));
        crate.Inventory.SetItem(0, new ItemStack(ItemsRegistry.Diamond, 5));

        // A loot function waits for work under the tick lock, as explorer maps do when chunks publish. Saving the
        // same plugin container must finish meanwhile, with the pending table rather than a partial fill.
        var rolls = 0;
        crate.LootTable = "probe:snapshot";
        PluginLootTables.Apply([new LootTable
        {
            Identifier = crate.LootTable,
            Pools =
            [
                new LootPool
                {
                    Rolls = new ConstantNumber { Value = 1 },
                    Entries = [new ItemEntry { Name = ItemsRegistry.Apple }]
                }
            ],
            Functions = [new SnapshotLootFunction(() =>
            {
                rolls++;
                Task.Run(() =>
                {
                    lock (context.World.Fluids.TickLock)
                    {
                        var saved = BlockEntityNbt.Save(crate)!;
                        Assert.True(saved.TryGetTagValue<string>("LootTable", out var pending));
                        Assert.Equal("probe:snapshot", pending);
                        Assert.False(saved.HasTag("Items"));
                    }
                }).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            })]
        }]);
        try
        {
            var access = context.World.GetItemAccess(position, null)!;
            Assert.Equal(5, access.GetItem(0)!.Count);
            Assert.Equal(5, access.GetItem(0)!.Count);
            Assert.Equal(1, rolls);
            Assert.Null(crate.LootTable);
            Assert.Equal(1, crate.Inventory.OfType<ItemStack>().Where(item => item.Is(ItemsRegistry.Apple))
                .Sum(item => item.Count));
        }
        finally
        {
            PluginLootTables.Apply([]);
        }

        for (var slot = 1; slot < crate.Inventory.Size; slot++)
            crate.Inventory.SetItem(slot, null);

        // The plugin's last slot holds one item through simulation, real insertion and hopper transfers.
        var limited = context.World.GetItemAccess(position, BlockFace.Down)!;
        var stones = new ItemStack(ItemsRegistry.Stone, 5);
        var revision = crate.Inventory.Revision;
        Assert.Equal(4, limited.Insert(0, stones, simulate: true)?.Count);
        Assert.Equal(revision, crate.Inventory.Revision);
        Assert.Null(crate.Inventory[8]);
        Assert.Equal(4, limited.Insert(0, stones)?.Count);
        Assert.Equal(1, crate.Inventory[8]?.Count);
        Assert.Same(stones, limited.Insert(0, stones));
        Assert.Equal(5, stones.Count);
        Assert.Equal(1, limited.Extract(0, 64)?.Count);

        crate.Inventory.SetItem(6, new ItemStack(ItemsRegistry.Stone, 3));
        var source = context.World.GetItemAccess(position, null)!;
        Assert.True(Hoppers.TryMoveOne(source, 6, limited, out var leftover));
        Assert.Null(leftover);
        Assert.Equal(1, crate.Inventory[8]?.Count);
        Assert.False(Hoppers.TryMoveOne(source, 6, limited, out leftover));
        Assert.Null(leftover);
        Assert.Equal(2, crate.Inventory[6]?.Count);
        crate.Inventory.SetItem(6, null);
        crate.Inventory.SetItem(8, null);

        // Direct windows and remapped container-backed menu slots use the same limit; a menu may lower it further.
        await human.OpenInventoryAsync(crate.Inventory);
        Assert.Same(crate.Inventory, human.OpenedContainer);
        human.CarriedItem = new ItemStack(ItemsRegistry.Stone, 5);
        await MenuClicks.ClickAsync(context.Server, human, 8, 0, ClickType.Pickup);
        Assert.Equal(1, crate.Inventory[8]?.Count);
        Assert.Equal(4, human.CarriedItem?.Count);
        await MenuClicks.ClickAsync(context.Server, human, 8, 0, ClickType.Pickup);
        Assert.Equal(1, crate.Inventory[8]?.Count);
        Assert.Equal(4, human.CarriedItem?.Count);
        human.Inventory.SetItem(36, new ItemStack(ItemsRegistry.Dirt, 2));
        await MenuClicks.ClickAsync(context.Server, human, 8, 0, ClickType.Swap);
        Assert.True(crate.Inventory[8]!.Is(ItemsRegistry.Stone));
        Assert.Equal(1, crate.Inventory[8]?.Count);
        Assert.Equal(2, human.Inventory[36]?.Count);
        human.Inventory.SetItem(36, null);
        crate.Inventory.SetItem(8, null);

        var menuType = PluginRegistry.RegisterMenu(container, "probe:crate_slots", 1, 2);
        var menu = new MenuContainer(menuType, "Crate slots")
        {
            BlockEntity = crate,
            Slots =
            [
                new MenuSlot(crate.Inventory, 8),
                new MenuSlot(crate.Inventory, 7) { MaxStackSize = 2 }
            ]
        };
        await human.OpenInventoryAsync(menu);
        await MenuClicks.ClickAsync(context.Server, human, 0, 0, ClickType.Pickup);
        Assert.Equal(1, crate.Inventory[8]?.Count);
        Assert.Equal(3, human.CarriedItem?.Count);
        await MenuClicks.ClickAsync(context.Server, human, 1, 0, ClickType.Pickup);
        Assert.Equal(2, crate.Inventory[7]?.Count);
        Assert.Equal(1, human.CarriedItem?.Count);
        human.CarriedItem = null;
        crate.Inventory.SetItem(7, null);
        crate.Inventory.SetItem(8, null);
        await human.OpenInventoryAsync(crate.Inventory);

        // A default-interface lookup reads states from complete published chunks, without creating missing regions.
        ILevel level = context.World;
        var regions = context.World.RegionCount;
        var queued = context.World.ChunksToGenCount;
        Assert.Null(level.GetLoadedBlock(new Vector(-513, 70, -513)));
        Assert.Equal(regions, context.World.RegionCount);
        Assert.Equal(queued, context.World.ChunksToGenCount);
        Assert.Equal(crateBlock.DefaultState.GetHashCode(), level.GetLoadedBlock(position)?.GetHashCode());
        Assert.Equal(BlocksRegistry.Air.GetHashCode(), level.GetLoadedBlock(position + Vector.East)?.GetHashCode());
        Assert.Equal(BlocksRegistry.VoidAir.GetHashCode(),
            level.GetLoadedBlock(new Vector(4, context.World.MinY - 1, 4))?.GetHashCode());
        var background = await Task.Run(() => level.GetLoadedBlock(position));
        Assert.Equal(crateBlock.DefaultState.GetHashCode(), background?.GetHashCode());

        // Unloading retires the inventory before its last save, and closes the window showing it: the player can't
        // take the diamonds the plugin's reload brings back.
        await manager.UnloadPluginAsync(container);

        Assert.Null(human.OpenedContainer);
        Assert.True(crate.Inventory.IsRetired);
        var kept = Assert.IsType<DataBlockEntity>(chunk.GetBlockEntity(4, 70, 4));
        Assert.Equal(5, ((NbtList)kept.Data["Items"]).OfType<NbtCompound>().Single().GetInt("count"));

        await manager.LoadPluginAsync(path);
        await context.World.DoWorldTickAsync();

        var restored = Assert.IsAssignableFrom<ContainerBlockEntity>(chunk.GetBlockEntity(4, 70, 4));
        Assert.Equal(5, restored.Inventory[0]!.Count);
        Assert.False(restored.Inventory.IsRetired);

        // Only the reloaded crate was live on a tick: its OnLoad added the plugin's level ticker, which ran then.
        Assert.Equal(1, AppContext.GetData(ProbeCrate.LoadsKey));
        Assert.Equal(1, AppContext.GetData(ProbeCrate.TicksKey));

        // Generation keeps a plugin's block entities as data, outside the level's tick; the level makes them the
        // plugin's once their chunk is complete.
        var generating = new Chunk(1, 0);
        var generated = new Vector(20, 70, 4);
        generating.SetBlock(generated.X, generated.Y, generated.Z, crateBlock.DefaultState);
        DataBlockEntity.ApplyBlockChange(generating, generated, crateBlock.DefaultState);
        context.World.LoadRegionByChunk(1, 0).SetChunk(generating);

        Assert.IsType<DataBlockEntity>(generating.GetBlockEntity(20, 70, 4));
        Assert.Null(level.GetLoadedBlock(generated));

        generating.SetChunkStatus(ChunkGenStage.full);
        await context.World.DoWorldTickAsync();

        Assert.Equal(crateBlock.DefaultState.GetHashCode(), level.GetLoadedBlock(generated)?.GetHashCode());
        Assert.IsAssignableFrom<ContainerBlockEntity>(generating.GetBlockEntity(20, 70, 4));
        Assert.Equal(2, AppContext.GetData(ProbeCrate.LoadsKey));
        Assert.Equal(2, AppContext.GetData(ProbeCrate.TicksKey));

        // Both live crates heard the items they joined with, and the unloaded crate's clicks and item access nothing.
        // A click is heard at the tick's end; item access too, once, and a tick with only SetChanged isn't.
        await human.OpenInventoryAsync(restored.Inventory);
        human.CarriedItem = new ItemStack(ItemsRegistry.Stone, 1);
        await MenuClicks.ClickAsync(context.Server, human, 1, 0, ClickType.Pickup);
        await context.World.DoWorldTickAsync();
        Assert.Equal(3, AppContext.GetData(ProbeCrate.ItemsChangedKey));

        context.World.GetItemAccess(position, null)!.Extract(1, 1);
        await context.World.DoWorldTickAsync();
        restored.SetChanged();
        await context.World.DoWorldTickAsync();
        Assert.Equal(4, AppContext.GetData(ProbeCrate.ItemsChangedKey));

        // A remote edit is saved immediately, including outside simulation distance and before another tick.
        human.Position = new VectorD(4096, 70, 4096);
        restored.Inventory.SetItem(0, new ItemStack(ItemsRegistry.Diamond, 2));
        var remoteSave = BlockEntityNbt.Save(restored)!;
        Assert.Equal(2, ((NbtList)remoteSave["Items"]).OfType<NbtCompound>().Single().GetInt("count"));

        // Chunk eviction waits for an in-flight click, freezes its final contents and closes remote viewers.
        await human.OpenInventoryAsync(restored.Inventory);
        var region = Assert.IsType<Region>(context.World.LoadRegionByChunk(0, 0));
        var closes = 0;
        context.EventDispatcher.RegisterEvent(Assert.Single(manager.Plugins),
            (Func<ContainerClosedEventArgs, ValueTask>)(async args =>
            {
                if (!ReferenceEquals(args.Container, restored.Inventory))
                    return;

                Assert.True(restored.Inventory.IsRetired);
                await human.OpenInventoryAsync(restored.Inventory);
                await region.FlushAsync();
                closes++;
            }));
        Task eviction;
        using (await restored.Inventory.LockAsync())
        {
            eviction = Task.Run(() => region.UnloadChunk(0, 0));
            restored.Inventory.SetItem(0, new ItemStack(ItemsRegistry.Diamond, 1));
            Assert.False(eviction.IsCompleted);
        }

        await eviction;
        Assert.Equal(1, closes);
        Assert.True(restored.Inventory.IsRetired);
        Assert.Null(human.OpenedContainer);
        await human.OpenInventoryAsync(restored.Inventory);
        Assert.Null(human.OpenedContainer);

        // The crate heard its chunk unload while still in its level; the loaded-only lookup doesn't load it back.
        Assert.Equal(position, AppContext.GetData(ProbeCrate.UnloadedKey));
        Assert.Null(context.World.GetLoadedBlockEntity(position));
        Assert.Null(level.GetLoadedBlock(position));
        Assert.Null(context.World.GetLoadedChunk(0, 0));

        var loaded = Assert.IsType<Chunk>(await region.GetChunkAsync(0, 0));
        await context.World.DoWorldTickAsync();
        var afterEviction = Assert.IsAssignableFrom<ContainerBlockEntity>(loaded.GetBlockEntity(4, 70, 4));
        Assert.Equal(1, afterEviction.Inventory[0]!.Count);
        Assert.False(afterEviction.Inventory.IsRetired);
        Assert.Same(afterEviction, context.World.GetLoadedBlockEntity(position));
        Assert.Equal(crateBlock.DefaultState.GetHashCode(), level.GetLoadedBlock(position)?.GetHashCode());
        Assert.Equal(3, AppContext.GetData(ProbeCrate.LoadsKey));
        Assert.Equal(6, AppContext.GetData(ProbeCrate.TicksKey));

        // The unloaded plugin's data still drops its items when any path removes the block.
        var livePlugin = Assert.Single(manager.Plugins);
        var keeping = new ContainerBlockEntity(new Container(9))
        {
            DropsContents = false,
            Owner = livePlugin
        };
        keeping.Inventory.SetItem(0, new ItemStack(ItemsRegistry.Diamond, 1));
        await context.World.SetBlockEntity(position, keeping);
        PluginLootTables.Apply([LootTableJson.Parse("""
            {"pools":[{"rolls":1,"entries":[{"type":"minecraft:item","name":"minecraft:barrel",
              "functions":[{"function":"minecraft:copy_components","source":"block_entity",
                "include":["minecraft:container"]}]}]}]}
            """, "probe:blocks/crate")]);
        await using (var capture = await BlockEntityComponents.CaptureAsync(context.World, position, human))
        {
            var boxDrop = Assert.Single(BlockLoot.GetDrops(
                crateBlock.DefaultState,
                null,
                new LegacyRandomSource(1),
                checkTool: false,
                blockEntityComponents: capture.Components));
            var held = boxDrop.GetComponent<SimpleDataComponent<ImmutableArray<ItemStack>>>(
                DataComponentType.Container);
            Assert.Equal(1, Assert.Single(held!.Value).Count);
        }

        await manager.UnloadPluginAsync(livePlugin);
        Assert.Null(PluginLootTables.Resolve("probe:blocks/crate"));

        // Unloading the plugin removed its ticker.
        await context.World.DoWorldTickAsync();
        Assert.Equal(6, AppContext.GetData(ProbeCrate.TicksKey));
        var keptBox = Assert.IsType<DataBlockEntity>(loaded.GetBlockEntity(4, 70, 4));
        Assert.False(((NbtCompound)keptBox.Data["obsidian:container"]).GetBool("drops_contents"));
        await context.World.ExplodeAsync((VectorD)position + 0.5, 2, false,
            AbstractLevel.ExplosionInteraction.Block);
        Assert.Null(loaded.GetBlockEntity(4, 70, 4));
        Assert.Equal(1, ItemsIn(context.World).Where(drop => drop.Item.Is(ItemsRegistry.Diamond))
            .Sum(drop => drop.Item.Count));

        // Removing after the outer liveness check must be revalidated inside the plugin call guard.
        var racePlugin = (await manager.LoadPluginAsync(path))!;
        var racePosition = new Vector(6, 70, 4);
        await context.World.SetBlockAsync(racePosition, crateBlock.DefaultState);
        var raceInventory = new CallbackInventory();
        var race = new CallbackContainer(raceInventory)
        {
            Owner = racePlugin
        };
        await context.World.SetBlockEntity(racePosition, race);
        raceInventory.BeforeRevision = () => context.World.SetBlockAsync(racePosition, BlocksRegistry.Air)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5))
            .GetAwaiter()
            .GetResult();
        await context.World.DoWorldTickAsync();
        Assert.True(race.IsRemoved);
        Assert.Equal(0, race.Calls);

        // Once a callback starts, concurrent removal finishes after it, without exposing IsRemoved during it.
        await context.World.SetBlockAsync(racePosition, crateBlock.DefaultState);
        var active = new CallbackContainer(new Container(9))
        {
            Owner = racePlugin
        };
        await context.World.SetBlockEntity(racePosition, active);
        Task? concurrentRemoval = null;
        var removalStarted = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        var removalFinishedDuringCall = false;
        var observedRemoved = false;
        active.OnChange = () =>
        {
            concurrentRemoval = Task.Run(async () =>
            {
                var pending = context.World.SetBlockAsync(racePosition, BlocksRegistry.Air).AsTask();
                removalStarted.SetResult(pending);
                await pending;
            });
            var pending = removalStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            removalFinishedDuringCall = pending.IsCompleted;
            observedRemoved = active.IsRemoved;
        };
        await context.World.DoWorldTickAsync();
        Assert.Null(loaded.GetBlockEntity(racePosition.X, racePosition.Y, racePosition.Z));
        Assert.False(removalFinishedDuringCall);
        Assert.False(observedRemoved);
        Assert.Equal(1, active.Calls);
        await concurrentRemoval!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(active.IsRemoved);
    }

    private sealed class CallbackInventory() : BaseContainer(1)
    {
        public Action? BeforeRevision { get; set; }

        public override long Revision
        {
            get
            {
                var code = this.BeforeRevision;
                this.BeforeRevision = null;
                code?.Invoke();
                return base.Revision;
            }
        }
    }

    private sealed class CallbackContainer(BaseContainer inventory) : ContainerBlockEntity(inventory)
    {
        public int Calls { get; private set; }

        public Action? OnChange { get; set; }

        protected internal override void OnItemsChanged()
        {
            this.Calls++;
            this.OnChange?.Invoke();
        }
    }

    [Fact(DisplayName = "A container's removal takes its items under the click lock, and closes its windows",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task ContainerRemovalTakesItsItemsAtomically()
    {
        await using var context = TestServerFactory.CreateServer($"container-removal-{Guid.NewGuid():N}");
        var chunk = AddEmptyChunk(context.World);
        var human = await JoinAsync(context);

        var position = new Vector(4, 70, 4);
        await context.World.SetBlockAsync(position, BlocksRegistry.Chest);
        var chest = Assert.IsAssignableFrom<ContainerBlockEntity>(chunk.GetBlockEntity(4, 70, 4));
        chest.Inventory.SetItem(0, new ItemStack(ItemsRegistry.Stone, 20));
        chest.Inventory.SetItem(1, new ItemStack(ItemsRegistry.Apple, 3));
        await human.OpenInventoryAsync(chest.Inventory);

        // A click holds the lock: the removal and a chunk save wait for it, so neither sees the click half applied.
        Task removal;
        Task<NbtCompound> save;
        using (await chest.Inventory.LockAsync())
        {
            removal = context.World.SetBlockAsync(position, BlocksRegistry.Air).AsTask();
            save = Task.Run(() => BlockEntityNbt.Save(chest)!);

            await Task.Delay(100);
            Assert.False(removal.IsCompleted);
            Assert.False(save.IsCompleted);
            Assert.True(chest.Inventory.HasItems());
        }

        await removal;
        var saved = await save;

        Assert.True(chest.Inventory.IsRetired);
        Assert.False(chest.Inventory.HasItems());
        Assert.Null(human.OpenedContainer);
        // The save took either side of the removal, never half of it.
        Assert.True(((NbtList)saved["Items"]).Count is 0 or 2);

        var drops = ItemsIn(context.World);
        Assert.Equal(20, drops.Where(drop => drop.Item.Is(ItemsRegistry.Stone)).Sum(drop => drop.Item.Count));
        Assert.Equal(3, drops.Where(drop => drop.Item.Is(ItemsRegistry.Apple)).Sum(drop => drop.Item.Count));

        // A retired container doesn't open again.
        await human.OpenInventoryAsync(chest.Inventory);
        Assert.Null(human.OpenedContainer);
    }

    [Fact(DisplayName = "A feature placed over a chest puts it back when rolled back, and drops its items when kept",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task LiveFeaturesKeepReplacedBlockEntitiesUntilCommitted()
    {
        await using var context = TestServerFactory.CreateServer($"live-feature-chests-{Guid.NewGuid():N}");
        var chunk = AddEmptyChunk(context.World);
        await JoinAsync(context);

        var position = new Vector(4, 70, 4);
        await context.World.SetBlockAsync(position, BlocksRegistry.Chest);
        var chest = Assert.IsAssignableFrom<ContainerBlockEntity>(chunk.GetBlockEntity(4, 70, 4));
        chest.Inventory.SetItem(0, new ItemStack(ItemsRegistry.Diamond, 2));

        var rolledBack = new LiveFeatureLevel(context.World);
        rolledBack.SetBlock(position, BlocksRegistry.Stone);
        Assert.Null(chunk.GetBlockEntity(4, 70, 4));
        rolledBack.Rollback();

        Assert.Same(chest, chunk.GetBlockEntity(4, 70, 4));
        Assert.False(chest.IsRemoved);
        Assert.Equal(2, chest.Inventory[0]!.Count);

        var kept = new LiveFeatureLevel(context.World);
        kept.SetBlock(position, BlocksRegistry.Stone);
        kept.Commit();

        Assert.True(chest.IsRemoved);
        await context.World.DoWorldTickAsync();
        var diamonds = ItemsIn(context.World).Where(drop => drop.Item.Is(ItemsRegistry.Diamond));
        Assert.Equal(2, diamonds.Sum(drop => drop.Item.Count));

        // What the level saves and sends of a block entity is a copy taken on its tick, which later changes to what
        // the block entity handed over don't reach.
        var retaining = new RetainingBlockEntity { Id = "othermod:machine" };
        await context.World.SetBlockEntity(position, retaining);
        retaining.SetChanged();
        retaining.SendUpdate();
        await context.World.DoWorldTickAsync();

        retaining.Retained.Add(new NbtTag<int>("later", 1));
        Assert.False(((NbtCompound)BlockEntityNbt.Save(retaining)!["state"]).HasTag("later"));
        Assert.False(((NbtCompound)retaining.ClientData!["state"]).HasTag("later"));

        // A change announced between ticks is taken in by a save with no tick before it, which sends nothing.
        retaining.SetChanged();
        await context.World.FlushRegionsAsync();
        Assert.True(((NbtCompound)BlockEntityNbt.Save(retaining)!["state"]).HasTag("later"));
        Assert.False(((NbtCompound)retaining.ClientData!["state"]).HasTag("later"));
    }

    [Fact(DisplayName = "Broken block entities give their loot what they hold: a shulker box keeps its items",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task BrokenBlockEntitiesGiveTheirLootWhatTheyHold()
    {
        await using var context = TestServerFactory.CreateServer($"block-entity-loot-{Guid.NewGuid():N}");
        var chunk = AddEmptyChunk(context.World);
        var human = await JoinAsync(context);
        human.GameMode = GameMode.Survival;

        var position = new Vector(4, 70, 4);
        await context.World.SetBlockAsync(position, BlocksRegistry.ShulkerBox);
        var box = Assert.IsAssignableFrom<ContainerBlockEntity>(chunk.GetBlockEntity(4, 70, 4));
        box.Inventory.SetItem(2, new ItemStack(ItemsRegistry.Diamond, 3));
        box.Inventory.CustomName = ChatMessage.Simple("Loot");

        await context.EventDispatcher.ExecuteEventAsync(new BlockBreakEventArgs(context.Server, human,
            BlocksRegistry.ShulkerBox, position, context.World));

        // Like vanilla's shulker box table: the box item copies the name and the items, which then don't drop.
        var dropped = Assert.Single(ItemsIn(context.World)).Item;
        Assert.True(dropped.Is(ItemsRegistry.ShulkerBox));
        Assert.Equal("Loot", dropped.GetComponent<SimpleDataComponent<ChatMessage>>(DataComponentType.CustomName)!
            .Value!.Text);
        AssertHoldsDiamonds(dropped);

        // The items are saved with the box item, and a box placed from it gets them back.
        var reloaded = dropped.ToNbt().ItemFromNbt()!;
        AssertHoldsDiamonds(reloaded);

        var placed = new Vector(6, 70, 4);
        await context.World.SetBlockAsync(placed, BlocksRegistry.ShulkerBox);
        await BlockEntityComponents.ApplyAsync(context.World, placed, reloaded);

        var refilled = Assert.IsAssignableFrom<ContainerBlockEntity>(chunk.GetBlockEntity(6, 70, 4));
        Assert.Equal(3, refilled.Inventory[2]!.Count);
        Assert.Equal("Loot", refilled.Inventory.CustomName!.Text);

        // A creative player's filled box still drops, holding its items.
        foreach (var item in ItemsIn(context.World))
            await context.World.DestroyEntityAsync(item);

        human.GameMode = GameMode.Creative;
        await context.EventDispatcher.ExecuteEventAsync(new BlockBreakEventArgs(context.Server, human,
            BlocksRegistry.ShulkerBox, placed, context.World));
        AssertHoldsDiamonds(Assert.Single(ItemsIn(context.World)).Item);

        // A cracked decorated pot drops its sherds through the table's dynamic entry: bricks for the plain faces.
        foreach (var item in ItemsIn(context.World))
            await context.World.DestroyEntityAsync(item);

        human.GameMode = GameMode.Survival;
        var pot = BlocksRegistry.DecoratedPot.WithProperty("cracked", "true");
        await context.World.SetBlockAsync(position, pot);
        var potEntity = Assert.IsType<DataBlockEntity>(chunk.GetBlockEntity(4, 70, 4));
        var decoratedSherd = new ItemStack(ItemsRegistry.AnglerPotterySherd, 1, ComponentBuilder.CustomData with
        {
            Value = new NbtCompound { new NbtTag<int>("pattern", 7) }
        });
        var filledPot = new ItemStack(ItemsRegistry.DecoratedPot, 1,
            ComponentBuilder.Container with { Value = [new ItemStack(ItemsRegistry.Diamond, 7)] },
            ComponentBuilder.PotDecorations with
            {
                Value = [decoratedSherd, new ItemStack(ItemsRegistry.Brick), new ItemStack(ItemsRegistry.Brick), new ItemStack(ItemsRegistry.Brick)]
            });
        await BlockEntityComponents.ApplyAsync(context.World, position, filledPot);

        await context.EventDispatcher.ExecuteEventAsync(new BlockBreakEventArgs(context.Server, human,
            pot, position, context.World));

        var pieces = ItemsIn(context.World).Select(drop => drop.Item).ToList();
        var droppedSherd = Assert.Single(pieces, item => item.Is(ItemsRegistry.AnglerPotterySherd));
        Assert.Equal(7, droppedSherd.GetComponent<SimpleDataComponent<NbtCompound>>(DataComponentType.CustomData)!.Value.GetInt("pattern"));
        Assert.Equal(3, pieces.Count(item => item.Is(ItemsRegistry.Brick)));
        Assert.Equal(7, pieces.Where(item => item.Is(ItemsRegistry.Diamond)).Sum(item => item.Count));
        Assert.False(potEntity.Data.HasTag("item"));

        // Replacing an intact pot uses the same spill path, without relying on its loot table.
        await context.World.SetBlockAsync(position, BlocksRegistry.DecoratedPot);
        await BlockEntityComponents.ApplyAsync(context.World, position, filledPot);
        await context.World.SetBlockAsync(position, BlocksRegistry.Stone);
        await context.World.SetBlockAsync(position, BlocksRegistry.Air);
        Assert.Equal(14, ItemsIn(context.World).Where(drop => drop.Item.Is(ItemsRegistry.Diamond))
            .Sum(drop => drop.Item.Count));

        static void AssertHoldsDiamonds(ItemStack item)
        {
            var contents = item.GetComponent<SimpleDataComponent<ImmutableArray<ItemStack>>>(
                DataComponentType.Container)!.Value;
            Assert.Equal(3, contents.Length);
            Assert.True(contents[2].Is(ItemsRegistry.Diamond));
            Assert.Equal(3, contents[2].Count);
        }
    }

    private sealed class SnapshotLootFunction(Action snapshot) : LootFunction
    {
        protected override ItemStack Run(ItemStack stack, LootContext context)
        {
            snapshot();
            return stack;
        }
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

    internal static async Task<Obsidian.Entities.Player> JoinAsync(RealServerContext context)
    {
        var human = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Human", (VectorD)origin);
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, human);

        return human;
    }

    // An empty plains chunk where the test's player stands, complete, so that it ticks.
    internal static Chunk AddEmptyChunk(World world, int chunkX = 0, int chunkZ = 0)
    {
        var chunk = new Chunk(chunkX, chunkZ);

        for (var x = 0; x < 16; x += 4)
        {
            for (var z = 0; z < 16; z += 4)
            {
                for (var y = -64; y < 320; y += 4)
                    chunk.SetBiome(x, y, z, CodecRegistry.Biomes.Plains);
            }
        }

        chunk.SetChunkStatus(ChunkGenStage.full);
        world.LoadRegionByChunk(chunkX, chunkZ).SetChunk(chunk);

        return chunk;
    }
}

/// <summary>
/// A block entity that hands the server compounds it keeps changing, to show the server keeps copies.
/// </summary>
public sealed class RetainingBlockEntity : BlockEntity
{
    public NbtCompound Retained { get; } = new("state");

    protected internal override void Save(NbtCompound tag) => tag.Add(this.Retained);

    protected internal override NbtCompound? GetUpdateData() => new() { this.Retained };
}

/// <summary>
/// The probe plugin's container block entity: nine slots, saved like a chest's. It counts its loads, and records where
/// its chunk unloaded, in <see cref="AppContext"/> data; the first one loaded adds a level ticker counting ticks.
/// </summary>
public sealed class ProbeCrate() : ContainerBlockEntity(new Container(9) { Title = "Crate" })
{
    public const string LoadsKey = "Obsidian.Tests.Crate.Loads";
    public const string UnloadedKey = "Obsidian.Tests.Crate.Unloaded";
    public const string TicksKey = "Obsidian.Tests.Crate.Ticks";
    public const string ItemsChangedKey = "Obsidian.Tests.Crate.ItemsChanged";

    // The plugin's own static, so each load of the plugin adds one ticker, like a network runtime would.
    private static IDisposable? ticker;

    protected internal override IReadOnlyList<int> GetSlots(BlockFace? side) =>
        side == BlockFace.Down ? [8] : base.GetSlots(side);

    protected internal override int SlotLimit(int slot, ItemStack stack)
    {
        if (slot != 8)
            return base.SlotLimit(slot, stack);

        return stack.Is(ItemsRegistry.Dirt) ? 0 : 1;
    }

    protected internal override void OnLoad()
    {
        Count(LoadsKey);
        ticker ??= this.Level!.AddTicker(CountTick);
    }

    protected internal override void OnChunkUnloaded() =>
        AppContext.SetData(UnloadedKey, this.Level is null ? null : this.BlockPosition);

    protected internal override void OnItemsChanged() => Count(ItemsChangedKey);

    private static ValueTask CountTick()
    {
        Count(TicksKey);
        return ValueTask.CompletedTask;
    }

    private static void Count(string key) => AppContext.SetData(key, (AppContext.GetData(key) as int? ?? 0) + 1);
}

/// <summary>
/// The probe's two parts, ordered far before near to exercise nearest-hit selection. It records whether it still had
/// its front part when it was removed in <see cref="AppContext"/> data, since its statics are its plugin's own.
/// </summary>
public sealed class ProbeParts : BlockEntity
{
    public const string RemovedKey = "Obsidian.Tests.Parts.Removed";
    public const string CollectedKey = "Obsidian.Tests.Parts.Collected";

    public bool Front { get; set; } = true;

    protected internal override void OnRemoved() => AppContext.SetData(RemovedKey, this.Front);

    protected internal override void CollectComponents(ItemStack item) => AppContext.SetData(CollectedKey, true);

    protected internal override void Load(NbtCompound tag) => this.Front = tag.GetBool("front");

    protected internal override void Save(NbtCompound tag) =>
        tag.Add(new NbtTag<byte>("front", this.Front ? (byte)1 : (byte)0));

    protected internal override NbtCompound? GetUpdateData() =>
        new() { new NbtTag<byte>("front", this.Front ? (byte)1 : (byte)0) };

    protected internal override BlockShapes? GetShapes()
    {
        BoundingBox[] boxes = this.Front
            ? [new(new VectorD(0.375, 0, 0.625), new VectorD(0.625, 1, 0.875)),
                new(new VectorD(0.375, 0, 0.125), new VectorD(0.625, 1, 0.375))]
            : [new(new VectorD(0.375, 0, 0.625), new VectorD(0.625, 1, 0.875))];

        return new BlockShapes(boxes, boxes);
    }
}

public sealed class ProbePartsBehavior : IBlockBehavior
{
    public const string UseKey = "Obsidian.Tests.Parts.Use";
    public const string BreakKey = "Obsidian.Tests.Parts.Break";

    public IBlock UpdateShape(ShapeUpdateContext context) =>
        context.Direction == BlockFace.Down && context.NeighborState.IsAir ? BlocksRegistry.Air : context.State;

    public ValueTask<bool> UseAsync(BlockUseContext context)
    {
        AppContext.SetData(UseKey, context.ClickedAt);
        return ValueTask.FromResult(true);
    }

    public async ValueTask BreakAsync(BlockBreakContext context)
    {
        AppContext.SetData(BreakKey, context.Hit);
        if (await context.Player.Level.GetBlockEntityAsync(context.Hit.Position) is ProbeParts { Front: true } parts)
        {
            parts.Front = false;
            parts.SetChanged();
            parts.SendUpdate();
            context.IsCancelled = true;
            context.ExtraDrops.Add(new ItemStack(ItemsRegistry.Diamond, 2));
        }
    }
}
