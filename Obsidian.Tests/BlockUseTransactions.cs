using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.API.Utilities;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ServerPlayer = Obsidian.Entities.Player;

namespace Obsidian.Tests;

public sealed class BlockUseTransactions
{
    private static readonly Vector target = new(8, 64, 8);
    private const BlockUpdateFlags Quiet = BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace;

    [Fact]
    public async Task CompetingInsertionsKeepTheLosingItemAndCannotOverwriteTheWinner()
    {
        await using var context = TestServerFactory.CreateServer($"block-exchange-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(context.World);
        await context.World.GetBlockAsync(target);
        var alice = Player(context, "Alice");
        var bob = Player(context, "Bob");

        foreach (var (block, item) in new[]
        {
            (BlocksRegistry.Jukebox, ItemsRegistry.Get("minecraft:music_disc_cat")),
            (BlocksRegistry.Lectern, ItemsRegistry.WritableBook)
        })
        {
            context.World.BlockUpdates.SetBlock(target, block, Quiet);
            alice.Inventory.SetItem(alice.CurrentHeldItemSlot, new ItemStack(item, 1));
            bob.Inventory.SetItem(bob.CurrentHeldItemSlot, new ItemStack(item, 1));
            var first = Read(context, alice);
            var second = Read(context, bob);
            var behavior = BlocksRegistry.GetUseItemOnBehavior(block.RegistryId)!;

            Assert.Equal(UseResult.Success, await behavior.UseItemOnAsync(first));
            Assert.False((await behavior.UseItemOnAsync(second)).ConsumesAction());

            Assert.Null(alice.GetHeldItem());
            Assert.Equal(1, bob.GetHeldItem()?.Count);
            Assert.Equal(1, Stored(context).Count);
            Assert.Equal(item.Id, Stored(context).Holder.Id);

            if (block.RegistryId == BlockIds.Jukebox)
            {
                // Two ejections that read the same full jukebox release exactly one disc.
                bob.Inventory.SetItem(bob.CurrentHeldItemSlot, null);
                var ejectFirst = Read(context, bob);
                var ejectSecond = Read(context, bob);
                Assert.Equal(UseResult.Success, await behavior.UseWithoutItemAsync(ejectFirst));
                Assert.False((await behavior.UseWithoutItemAsync(ejectSecond)).ConsumesAction());
                Assert.Single(context.World.GetEntitiesInRange((VectorD)target, 4).OfType<ItemEntity>());
            }
        }
    }

    [Fact]
    public async Task InterruptedInsertionsPreserveBothTheHandAndTheDestination()
    {
        await using var context = TestServerFactory.CreateServer($"stale-exchange-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(context.World);
        await context.World.GetBlockAsync(target);
        var player = Player(context, "User");
        var slot = player.CurrentHeldItemSlot;

        foreach (var interference in new[] { "hand", "entity", "block", "death", "respawn", "busy" })
        {
            context.World.BlockUpdates.SetBlock(target, BlocksRegistry.Air, Quiet);
            context.World.BlockUpdates.SetBlock(target, BlocksRegistry.Jukebox, Quiet);
            player.Health = 20;
            player.Respawning = false;
            var disc = new ItemStack(ItemsRegistry.Get("minecraft:music_disc_cat"), 1);
            player.Inventory.SetItem(slot, disc);
            var use = Read(context, player);
            IDisposable heldLock = null;
            switch (interference)
            {
                case "hand":
                    player.Inventory.SetItem(slot, new ItemStack(ItemsRegistry.Diamond, 2));
                    break;
                case "entity":
                    context.World.BlockUpdates.SetBlock(target, BlocksRegistry.Air, Quiet);
                    context.World.BlockUpdates.SetBlock(target, BlocksRegistry.Jukebox, Quiet);
                    break;
                case "block":
                    context.World.BlockUpdates.SetBlock(target, BlocksRegistry.Stone, Quiet);
                    break;
                case "death":
                    player.Health = 0;
                    break;
                case "respawn":
                    player.Respawning = true;
                    break;
                case "busy":
                    heldLock = player.Inventory.Lock();
                    break;
            }

            try
            {
                var behavior = BlocksRegistry.GetUseItemOnBehavior(BlockIds.Jukebox)!;
                Assert.False((await behavior.UseItemOnAsync(use)).ConsumesAction(), interference);
            }
            finally
            {
                heldLock?.Dispose();
            }

            Assert.Equal(1, disc.Count);
            if (interference == "hand")
            {
                Assert.Equal(ItemsRegistry.Diamond.Id, player.GetHeldItem()?.Holder.Id);
                Assert.Equal(2, player.GetHeldItem()?.Count);
            }
            else
            {
                Assert.Same(disc, player.GetHeldItem());
            }

            if (context.World.GetResidentBlockEntity(target) is DataBlockEntity jukebox)
                Assert.False(jukebox.SnapshotData().HasTag("RecordItem"), interference);
        }
    }

    private static ServerPlayer Player(RealServerContext context, string name)
    {
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, name,
            new VectorD(8.5, 64, 10.5));
        player.GameMode = GameMode.Survival;
        return player;
    }

    internal static BlockUse Read(RealServerContext context, ServerPlayer player) => new()
    {
        Server = context.Server,
        Level = context.World,
        Player = player,
        Position = target,
        Block = context.World.BlockUpdates.GetBlock(target),
        BlockEntity = context.World.GetResidentBlockEntity(target),
        Face = BlockFace.Up,
        Cursor = new VectorD(0.5, 0.5, 0.5),
        Hand = InteractionHand.MainHand,
        HandSlot = player.CurrentHeldItemSlot,
        Item = player.GetHeldItem(),
        UsedItem = player.GetHeldItem() is ItemStack item ? new ItemStack(item, item.Count) : null,
        Sequence = 1
    };

    private static ItemStack Stored(RealServerContext context)
    {
        var entity = context.World.GetResidentBlockEntity(target);
        if (entity is LecternBlockEntity lectern)
            return Assert.IsType<ItemStack>(lectern.Lectern.Book);

        var jukebox = Assert.IsType<DataBlockEntity>(entity);
        Assert.True(jukebox.SnapshotData().TryGetTag<NbtCompound>("RecordItem", out var record));
        return record.ItemFromNbt();
    }
}
