using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Loot;
using Obsidian.API.Loot.Entries;
using Obsidian.API.Loot.Functions;
using Obsidian.API.Loot.Numbers;
using Obsidian.API.Registries;
using Obsidian.API.Utilities;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Serverbound;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.WorldData;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ServerPlayer = Obsidian.Entities.Player;

namespace Obsidian.Tests;

/// <summary>
/// Obsidian-only risks of brushing and armor stand use: the hand and the block or stand change together or not at
/// all, so items are neither duplicated nor lost when the hand, the inventory's lock, the block or the stand change
/// between the click and the commit, and both save in vanilla's form.
/// </summary>
[Collection(nameof(PluginTagFiles))]
public sealed class BrushingAndArmorStandTransactions
{
    private static readonly Vector target = new(8, 64, 8);

    private const BlockUpdateFlags Quiet = BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace;

    [Fact]
    public async Task StandUseRefusesAHandThatChangedSinceTheClick()
    {
        await using var context = await WorldAsync("stand-hand");
        var stand = Stand(context.World);
        var player = Player(context, "User", new ItemStack(ItemsRegistry.IronHelmet));

        await ClickAsync(context, player, stand, height: 1.8f);
        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.GoldenHelmet));
        await context.World.RunQueuedEntityActionsAsync();

        Assert.True(stand.GetEquipment(EquipmentSlot.Helmet).IsAir);
        Assert.Same(ItemsRegistry.GoldenHelmet, player.GetHeldItem()!.Holder);
    }

    [Fact]
    public async Task StandUseRefusesWhileTheInventoryIsBusy()
    {
        await using var context = await WorldAsync("stand-busy");
        var stand = Stand(context.World);
        var player = Player(context, "User", new ItemStack(ItemsRegistry.IronHelmet));

        await ClickAsync(context, player, stand, height: 1.8f);
        using (await player.Inventory.LockAsync())
            await context.World.RunQueuedEntityActionsAsync();

        Assert.True(stand.GetEquipment(EquipmentSlot.Helmet).IsAir);
        Assert.Same(ItemsRegistry.IronHelmet, player.GetHeldItem()!.Holder);

        // Once the inventory is free, a new click goes through.
        await ClickAsync(context, player, stand, height: 1.8f);
        await context.World.RunQueuedEntityActionsAsync();

        Assert.Same(ItemsRegistry.IronHelmet, stand.GetEquipment(EquipmentSlot.Helmet).Holder);
        Assert.Null(player.GetHeldItem());
    }

    [Fact]
    public async Task TwoPlayersUsingOneSlotAtOnceLeaveEachItemInOnePlace()
    {
        await using var context = await WorldAsync("stand-race");
        var stand = Stand(context.World);
        stand.SetEquipment(EquipmentSlot.Helmet, new ItemStack(ItemsRegistry.IronHelmet));
        var swapper = Player(context, "Swapper", new ItemStack(ItemsRegistry.GoldenHelmet));
        var taker = Player(context, "Taker", null);

        await Task.WhenAll(
            ClickAsync(context, swapper, stand, height: 1.8f),
            ClickAsync(context, taker, stand, height: 1.8f));
        await context.World.RunQueuedEntityActionsAsync();

        ItemStack?[] places = [swapper.GetHeldItem(), taker.GetHeldItem(), stand.GetEquipment(EquipmentSlot.Helmet)];
        var held = places.Where(item => item is { IsAir: false, Count: > 0 }).Select(item => item!.Holder).ToArray();
        Assert.Equal(2, held.Length);
        Assert.Single(held, holder => holder == ItemsRegistry.IronHelmet);
        Assert.Single(held, holder => holder == ItemsRegistry.GoldenHelmet);
    }

    [Fact]
    public async Task StandRemovedBeforeTheUseCommitsKeepsTheHand()
    {
        await using var context = await WorldAsync("stand-removed");
        var stand = Stand(context.World);
        stand.SetEquipment(EquipmentSlot.Helmet, new ItemStack(ItemsRegistry.DiamondHelmet));
        var player = Player(context, "User", new ItemStack(ItemsRegistry.IronHelmet));

        await ClickAsync(context, player, stand, height: 1.8f);
        await context.World.DestroyEntityAsync(stand);
        await context.World.RunQueuedEntityActionsAsync();

        Assert.Same(ItemsRegistry.IronHelmet, player.GetHeldItem()!.Holder);
        Assert.Same(ItemsRegistry.DiamondHelmet, stand.GetEquipment(EquipmentSlot.Helmet).Holder);
    }

    [Fact]
    public async Task StandRemovedWhileTheExchangeWaitsKeepsTheHand()
    {
        await using var context = await WorldAsync("stand-removal-race");
        var world = context.World;
        var stand = Stand(world);
        var player = Player(context, "User", new ItemStack(ItemsRegistry.IronHelmet));
        var use = await ArmorStandUse.CaptureAsync(player, InteractionHand.MainHand, new VectorF(0, 1.8f, 0.2f));

        // A removal under way holds the level's transfer lock. The use is let in once it holds the player's
        // inventory, so it is waiting for the transfer lock, then the stand goes before the removal lets go.
        using var removing = new System.Threading.ManualResetEventSlim();
        using var remove = new System.Threading.ManualResetEventSlim();
        var removed = false;
        var remover = new System.Threading.Thread(() =>
        {
            using (world.TransferLock.Enter())
            {
                removing.Set();
                remove.Wait();
                removed = world.RemoveEntity(stand, transferring: false);
            }
        });
        remover.Start();
        removing.Wait();

        var exchange = Task.Run(async () => await stand.UseAsync(use));
        var waiting = System.Threading.SpinWait.SpinUntil(
            () => InventoryBusy(player) || exchange.IsCompleted,
            TimeSpan.FromSeconds(10));
        Assert.True(waiting);
        remove.Set();
        remover.Join();
        await exchange;

        Assert.True(removed);
        Assert.Same(ItemsRegistry.IronHelmet, player.GetHeldItem()!.Holder);
        Assert.True(stand.GetEquipment(EquipmentSlot.Helmet).IsAir);
    }

    [Fact]
    public async Task BrushEmptiedWhileItsLootRollsDoesNotFinish()
    {
        await using var context = await WorldAsync("brush-emptied");
        var brush = new ItemStack(ItemsRegistry.Brush);
        var player = Player(context, "User", brush);
        var brushable = AlmostBrushed(context.World);
        brushable.SetLootTable(ProbeTable, 123);

        await StrokeWhileRollingAsync(context, player, brush, () => ChangeHeld(player, () => brush.Count = 0));

        Assert.Equal(BlockIds.SuspiciousSand, context.World.BlockUpdates.GetBlock(target).RegistryId);
        Assert.Equal(BrushableBlockEntity.StrokesToFinish - 1, brushable.BrushCount);
        Assert.Equal(ProbeTable, brushable.LootTable);
        Assert.Empty(Items(context.World));
    }

    [Fact]
    public async Task BrushChangedWhileItsLootRollsDoesNotFinish()
    {
        await using var context = await WorldAsync("brush-changed");
        var brush = new ItemStack(ItemsRegistry.Brush);
        var player = Player(context, "User", brush);
        var brushable = AlmostBrushed(context.World);
        brushable.SetLootTable(ProbeTable, 123);

        await StrokeWhileRollingAsync(context, player, brush, () => ChangeHeld(player,
            () => brush[DataComponentType.Unbreakable] = ComponentBuilder.Unbreakable with { Value = true }));

        Assert.Equal(BlockIds.SuspiciousSand, context.World.BlockUpdates.GetBlock(target).RegistryId);
        Assert.Equal(BrushableBlockEntity.StrokesToFinish - 1, brushable.BrushCount);
        Assert.Empty(Items(context.World));
        Assert.Equal(0, brush.Damage);
    }

    [Fact]
    public async Task LootRolledForAReplacedBlockIsNotGivenToItsReplacement()
    {
        await using var context = await WorldAsync("brush-replacement");
        var world = context.World;
        var brush = new ItemStack(ItemsRegistry.Brush);
        var player = Player(context, "User", brush);
        AlmostBrushed(world).SetLootTable(ProbeTable, 123);
        BrushableBlockEntity? replacement = null;

        // The same table and seed on suspicious gravel put where the sand was while the sand's loot rolls.
        await StrokeWhileRollingAsync(context, player, brush, () =>
        {
            world.BlockUpdates.SetBlock(target, BlocksRegistry.Stone);
            world.BlockUpdates.SetBlock(target, BlocksRegistry.SuspiciousGravel);
            replacement = (BrushableBlockEntity)world.GetResidentBlockEntity(target)!;
            replacement.SetLootTable(ProbeTable, 123);
        });

        Assert.NotNull(replacement);
        Assert.Same(replacement, world.GetResidentBlockEntity(target));
        Assert.Equal(0, replacement!.BrushCount);
        Assert.Equal(ProbeTable, replacement.LootTable);
        Assert.Null(replacement.Item);
        Assert.Empty(Items(world));
    }

    [Fact]
    public async Task PendingTeleportRefusesStandUseAndBrushStroke()
    {
        await using var context = await WorldAsync("pending-teleport");
        var stand = Stand(context.World);
        var player = Player(context, "User", new ItemStack(ItemsRegistry.IronHelmet));
        await ClickAsync(context, player, stand, height: 1.8f);
        await player.TeleportAsync(player.Position + new VectorD(0, 0, 0.5));
        Assert.True(player.HasPendingTeleport);

        await context.World.RunQueuedEntityActionsAsync();
        Assert.True(stand.GetEquipment(EquipmentSlot.Helmet).IsAir);

        var brush = new ItemStack(ItemsRegistry.Brush);
        player.Inventory.SetItem(player.CurrentHeldItemSlot, brush);
        var brushable = AlmostBrushed(context.World);
        await StrokeAsync(context, player, brush);

        Assert.Equal(BlockIds.SuspiciousSand, context.World.BlockUpdates.GetBlock(target).RegistryId);
        Assert.Equal(BrushableBlockEntity.StrokesToFinish - 1, brushable.BrushCount);
        Assert.Equal(0, brush.Damage);
    }

    [Fact]
    public async Task BusyInventorySkipsTheFinishingStrokeAndTheNextFinishesOnce()
    {
        await using var context = await WorldAsync("brush-busy");
        var brush = new ItemStack(ItemsRegistry.Brush);
        var player = Player(context, "User", brush);
        AlmostBrushed(context.World);

        using (await player.Inventory.LockAsync())
            await StrokeAsync(context, player, brush);

        Assert.Equal(BlockIds.SuspiciousSand, context.World.BlockUpdates.GetBlock(target).RegistryId);
        Assert.Empty(Items(context.World));
        Assert.Equal(0, brush.Damage);

        context.World.LevelData.GameTime += BrushableBlockEntity.CooldownTicks;
        await StrokeAsync(context, player, brush);

        Assert.Equal(BlockIds.Sand, context.World.BlockUpdates.GetBlock(target).RegistryId);
        Assert.Single(Items(context.World), item => item.Item.Holder == ItemsRegistry.Diamond);
        Assert.Equal(1, brush.Damage);
    }

    [Fact]
    public async Task BlockReplacedBeforeTheFinishingStrokeDropsNothing()
    {
        await using var context = await WorldAsync("brush-replaced");
        var brush = new ItemStack(ItemsRegistry.Brush);
        var player = Player(context, "User", brush);
        AlmostBrushed(context.World);

        context.World.BlockUpdates.SetBlock(target, BlocksRegistry.Stone);
        await StrokeAsync(context, player, brush);

        Assert.Equal(BlockIds.Stone, context.World.BlockUpdates.GetBlock(target).RegistryId);
        Assert.Empty(Items(context.World));
        Assert.Equal(0, brush.Damage);
    }

    [Fact]
    public async Task BrushMovedOutOfTheHandBeforeTheStrokeChangesNothing()
    {
        await using var context = await WorldAsync("brush-moved");
        var brush = new ItemStack(ItemsRegistry.Brush);
        var player = Player(context, "User", brush);
        var brushable = AlmostBrushed(context.World);

        player.Inventory.SetItem(player.CurrentHeldItemSlot, null);
        player.Inventory.SetItem(20, brush);
        await StrokeAsync(context, player, brush);

        Assert.Equal(BlockIds.SuspiciousSand, context.World.BlockUpdates.GetBlock(target).RegistryId);
        Assert.Equal(BrushableBlockEntity.StrokesToFinish - 1, brushable.BrushCount);
        Assert.Empty(Items(context.World));
        Assert.Equal(0, brush.Damage);
    }

    [Fact]
    public void BrushableBlockEntitySavesAndLoadsInVanillaForm()
    {
        // A loot table waiting to be rolled saves with its seed; a rolled item saves alone, without the face clients
        // are sent, as vanilla's saveAdditional writes them.
        var pending = new NbtCompound
        {
            new NbtTag<string>("LootTable", "minecraft:archaeology/desert_pyramid"),
            new NbtTag<long>("LootTableSeed", -4242L)
        };
        Assert.True(NbtJson.Same(NbtJson.ToJson(pending), NbtJson.ToJson(RoundTrip(pending))));

        var rolled = new NbtCompound
        {
            new ItemStack(ItemsRegistry.Emerald).ToNbt("item"),
            new NbtTag<int>("hit_direction", (int)BlockFace.North)
        };
        var saved = RoundTrip(rolled);
        Assert.False(saved.HasTag("hit_direction"));
        Assert.Equal("minecraft:emerald", ((NbtCompound)saved["item"]).GetString("id"));

        var brushable = new BrushableBlockEntity { Id = "minecraft:brushable_block" };
        brushable.Load(rolled);
        var client = brushable.GetUpdateData()!;
        Assert.Equal((int)BlockFace.North, client.GetInt("hit_direction"));
        Assert.Equal("minecraft:emerald", ((NbtCompound)client["item"]).GetString("id"));
    }

    [Fact]
    public async Task ArmorStandSavesAndLoadsInVanillaForm()
    {
        await using var context = await WorldAsync("stand-save");
        var saved = new NbtCompound
        {
            new NbtTag<string>("id", "minecraft:armor_stand"),
            EntityNbt.DoubleList("Pos", new VectorD(8.5, 64, 8.5)),
            new NbtCompound("equipment")
            {
                new ItemStack(ItemsRegistry.IronHelmet).ToNbt("head"),
                new ItemStack(ItemsRegistry.Stick, 1).ToNbt("mainhand"),
                new ItemStack(ItemsRegistry.DiamondHorseArmor).ToNbt("body"),
                new ItemStack(ItemsRegistry.Saddle).ToNbt("saddle")
            },
            new NbtTag<bool>("Invisible", false),
            new NbtTag<bool>("Small", true),
            new NbtTag<bool>("ShowArms", true),
            new NbtTag<int>("DisabledSlots", 4096),
            new NbtTag<bool>("NoBasePlate", true),
            new NbtTag<bool>("Marker", true),
            new NbtCompound("Pose")
            {
                new NbtList(NbtTagType.Float, "Head")
                {
                    new NbtTag<float>(string.Empty, 10),
                    new NbtTag<float>(string.Empty, 20),
                    new NbtTag<float>(string.Empty, 30)
                }
            }
        };

        var stand = Assert.IsType<ArmorStand>(EntityNbt.Load(saved, context.World));
        Assert.Same(ItemsRegistry.IronHelmet, stand.GetEquipment(EquipmentSlot.Helmet).Holder);
        Assert.Same(ItemsRegistry.Stick, stand.GetEquipment(EquipmentSlot.MainHand).Holder);
        Assert.True(stand.Small && stand.ShowArms && stand.NoBasePlate && stand.Marker);
        Assert.Equal(4096, stand.DisabledSlots);
        Assert.Equal(new VectorF(10, 20, 30), stand.HeadPose);

        var written = EntityNbt.Save(stand)!;
        foreach (var name in new[] { "equipment", "Invisible", "Small", "ShowArms", "DisabledSlots", "NoBasePlate",
            "Marker", "Pose" })
        {
            Assert.True(NbtJson.Same(NbtJson.ToJson(saved[name]), NbtJson.ToJson(written[name])), name);
        }

        // What changes after loading is what's saved, not what was loaded: an emptied hand and a false marker flag
        // leave the save, as vanilla leaves them out.
        stand.SetEquipment(EquipmentSlot.Helmet, new ItemStack(ItemsRegistry.GoldenHelmet));
        stand.SetEquipment(EquipmentSlot.MainHand, ItemStack.Air);
        stand.Marker = false;
        stand.HeadPose = new VectorF(0, 0, 0);
        var changed = EntityNbt.Save(stand)!;

        // The body and saddle a use can't fill on a stand are saved as vanilla saves them, from what was loaded.
        var equipment = new NbtCompound("equipment")
        {
            new ItemStack(ItemsRegistry.GoldenHelmet).ToNbt("head"),
            new ItemStack(ItemsRegistry.DiamondHorseArmor).ToNbt("body"),
            new ItemStack(ItemsRegistry.Saddle).ToNbt("saddle")
        };
        Assert.True(NbtJson.Same(NbtJson.ToJson(equipment), NbtJson.ToJson(changed["equipment"])));
        Assert.False(changed.HasTag("Marker"));
        Assert.Empty((NbtCompound)changed["Pose"]);
    }

    private const string ProbeTable = "probe:brushing_rendezvous";

    // A stroke whose loot runs `during` as it rolls, outside the level's locks, as a plugin's loot function could.
    private static async Task StrokeWhileRollingAsync(RealServerContext context, ServerPlayer player,
        ItemStack brush, Action during)
    {
        var table = new LootTable
        {
            Identifier = ProbeTable,
            Pools =
            [
                new LootPool
                {
                    Rolls = new ConstantNumber { Value = 1 },
                    Entries = [new ItemEntry { Name = ItemsRegistry.Diamond }]
                }
            ],
            Functions = [new DuringRoll(during)]
        };

        PluginLootTables.Apply([table]);
        try
        {
            await StrokeAsync(context, player, brush);
        }
        finally
        {
            PluginLootTables.Apply([]);
        }
    }

    // Changes the held brush under the inventory's lock, as a click would.
    private static void ChangeHeld(ServerPlayer player, Action change)
    {
        using (player.Inventory.Lock())
            change();
    }

    private static bool InventoryBusy(ServerPlayer player)
    {
        if (!player.Inventory.TryLock(out var handle))
            return true;

        handle.Dispose();
        return false;
    }

    private sealed class DuringRoll(Action during) : LootFunction
    {
        protected override ItemStack Run(ItemStack stack, LootContext context)
        {
            during();
            return stack;
        }
    }

    private static NbtCompound RoundTrip(NbtCompound saved)
    {
        var brushable = new BrushableBlockEntity { Id = "minecraft:brushable_block" };
        brushable.Load(saved);
        var written = new NbtCompound();
        brushable.Save(written);
        return written;
    }

    // Suspicious sand on stone holding a diamond, one stroke from finished.
    private static BrushableBlockEntity AlmostBrushed(WorldData.World world)
    {
        world.BlockUpdates.SetBlock(target + Vector.Down, BlocksRegistry.Stone, Quiet);
        world.BlockUpdates.SetBlock(target, BlocksRegistry.SuspiciousSand, Quiet);
        var brushable = Assert.IsType<BrushableBlockEntity>(world.GetResidentBlockEntity(target));
        brushable.Load(new NbtCompound { new ItemStack(ItemsRegistry.Diamond).ToNbt("item") });
        for (var stroke = 1; stroke < BrushableBlockEntity.StrokesToFinish; stroke++)
        {
            world.LevelData.GameTime += BrushableBlockEntity.CooldownTicks;
            brushable.Brush(world.LevelData.GameTime, BlockFace.Up);
        }

        world.LevelData.GameTime += BrushableBlockEntity.CooldownTicks;
        return brushable;
    }

    private static async Task StrokeAsync(RealServerContext context, ServerPlayer player, ItemStack brush)
    {
        var hit = new BlockShapeHit(target, new VectorD(0.5, 1, 0.5), BlockFace.Up, 0);
        await Brushing.StrokeAsync(context.World, player, brush, player.CurrentHeldItemSlot, hit);
        await context.World.RunQueuedEntityActionsAsync();
    }

    private static ItemEntity[] Items(WorldData.World world) =>
        world.GetNonPlayerEntitiesInRange((VectorD)target, 4).OfType<ItemEntity>().ToArray();

    private static async Task<RealServerContext> WorldAsync(string name)
    {
        var context = TestServerFactory.CreateServer($"{name}-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(context.World);
        await context.World.GetBlockAsync(target);
        context.World.BlockUpdates.SetBlock(target + Vector.Down, BlocksRegistry.Stone, Quiet);
        return context;
    }

    private static ArmorStand Stand(WorldData.World world)
    {
        var stand = new ArmorStand
        {
            Level = world,
            EntityId = Server.GetNextEntityId(),
            Position = (VectorD)target + new VectorD(0.5, 0, 0.5),
            Type = EntityType.ArmorStand
        };

        Assert.True(world.TryAddEntity(stand));
        return stand;
    }

    private static ServerPlayer Player(RealServerContext context, string name, ItemStack? held)
    {
        var position = (VectorD)target + new VectorD(0.5, 0, 2);
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, name, position);
        context.World.TryAddPlayer(player);
        player.GameMode = GameMode.Survival;
        player.Yaw = 180;
        player.Inventory.SetItem(player.CurrentHeldItemSlot, held);
        return player;
    }

    // The interact packet a client sends for a click on the stand at a height, which queues the use.
    private static async Task ClickAsync(RealServerContext context, ServerPlayer player, ArmorStand stand,
        float height)
    {
        var packet = new InteractPacket();
        typeof(InteractPacket).GetProperty(nameof(InteractPacket.EntityId))!.SetValue(packet, stand.EntityId);
        typeof(InteractPacket).GetProperty(nameof(InteractPacket.Hand))!.SetValue(packet, InteractionHand.MainHand);
        typeof(InteractPacket).GetProperty(nameof(InteractPacket.Location))!
            .SetValue(packet, new VectorF(0, height, 0.2f));
        await packet.HandleAsync(context.Server, player);
    }
}
