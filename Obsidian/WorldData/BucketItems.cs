using Obsidian.API.BlockEntities;
using Obsidian.API.Blocks;
using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.Entities;
using Obsidian.Entities.AI;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData.Fluids;

namespace Obsidian.WorldData;

/// <summary>Fluid buckets use the player's aim, independently of an earlier use-on packet.</summary>
internal static class BucketItems
{
    internal static bool Handles(ItemStack? stack) => stack is { Count: > 0 }
        && stack.Holder.Id is ItemIds.Bucket or ItemIds.WaterBucket or ItemIds.LavaBucket;

    internal static async ValueTask<UseResult> UseAsync(IServer server, Player player, InteractionHand hand,
        int sequence)
    {
        if (player.Level is not AbstractLevel level)
            return UseResult.Pass;

        int slot;
        ItemStack? stack;
        ItemStack? snapshot;
        using (await player.Inventory.LockAsync())
        {
            slot = hand == InteractionHand.OffHand ? 45 : player.CurrentHeldItemSlot;
            stack = player.Inventory.GetItem(slot);
            snapshot = stack is null ? null : new ItemStack(stack, stack.Count);
        }

        if (!Handles(stack) || player.IsOnCooldown(stack) || player.GameMode == GameMode.Spectator)
        {
            await player.SyncInventorySlotAsync(slot);
            return UseResult.Pass;
        }

        BlockUse? use = null;
        lock (level.Fluids.TickLock)
        {
            if (Trace(level, player, stack!.Holder.Id == ItemIds.Bucket) is BlockShapeHit hit)
            {
                use = new BlockUse
                {
                    Server = server,
                    Level = level,
                    Player = player,
                    Position = hit.Position,
                    Block = level.BlockUpdates.GetBlock(hit.Position),
                    BlockEntity = level.GetResidentBlockEntity(hit.Position),
                    Face = hit.Face,
                    Cursor = hit.ClickedAt,
                    Hand = hand,
                    HandSlot = slot,
                    Item = stack,
                    UsedItem = snapshot,
                    Sequence = sequence
                };
            }
        }

        if (use is null)
        {
            await player.SyncInventorySlotAsync(slot);
            return UseResult.Pass;
        }

        var result = UseResult.Fail;
        if (use.CanCommit && use.MayBuild)
        {
            var interact = new PlayerInteractEventArgs(player, server) { Item = stack, Hand = hand };
            await server.EventDispatcher.ExecuteEventAsync(interact);
            if (!interact.IsCancelled)
            {
                var plugin = Plugins.ItemBehaviors.CanUseItems(player)
                    && await Plugins.ItemBehaviors.UseAsync(new ItemUseContext
                    {
                        Player = player,
                        Stack = stack!,
                        Hand = hand
                    });
                if (plugin)
                    use.ChangedInventorySlots.Add(slot);

                result = plugin ? UseResult.Success : await ApplyAsync(use);
            }
        }

        if (result.Swings())
            player.SwingForUse(hand, snapshot, includeSelf: result == UseResult.SuccessServer);

        // A failed prediction may have changed both the traced block and its neighboring destination.
        foreach (var position in new[] { use.Position, use.Position + use.Face.ToVector() })
        {
            var block = level.GameEventBlock(position) ?? BlocksRegistry.Air;
            player.Client.SendPacket(new BlockUpdatePacket(position, block.GetHashCode()));
        }

        var changed = result.ConsumesAction() ? use.ChangedInventorySlots : [use.HandSlot];
        foreach (var changedSlot in changed)
            await player.SyncInventorySlotAsync(changedSlot);

        return result;
    }

    /// <summary>The captured hit and hand are validated together by BlockUse, including after plugin callbacks.</summary>
    internal static async ValueTask<UseResult> ApplyAsync(BlockUse use)
    {
        if (!use.CanCommit || !use.MayBuild || !Handles(use.Item))
            return UseResult.Fail;

        var result = use.Item!.Holder.Id == ItemIds.Bucket
            ? await PickupAsync(use) : await PourAsync(use);
        if (result.ConsumesAction())
        {
            use.Player.Statistics.Increment(use.UsedItem!.Holder.UnlocalizedName, type: "minecraft:used");
            if (use.Item.Holder.Id == ItemIds.Bucket)
                await use.Player.FireProgressAsync("filled_bucket", item: use.ExchangeRemainder);
        }

        return result;
    }

    private static async ValueTask<UseResult> PickupAsync(BlockUse use)
    {
        var block = use.Block;
        var fluid = FluidState.Of(block);
        var powder = block.Is(BlockIds.PowderSnow);
        var waterlogged = block.GetProperty("waterlogged") == "true";
        if (!powder && !waterlogged && !(block.IsLiquidBlock() && fluid.IsSource))
            return UseResult.Fail;

        var filled = ItemsRegistry.WaterBucket;
        var sound = "minecraft:item.bucket.fill";
        if (powder)
        {
            filled = ItemsRegistry.PowderSnowBucket;
            sound = "minecraft:item.bucket.fill_powder_snow";
        }
        else if (fluid.IsLava)
        {
            filled = ItemsRegistry.LavaBucket;
            sound = "minecraft:item.bucket.fill_lava";
        }

        var replacement = waterlogged ? block.WithProperty("waterlogged", false) : BlocksRegistry.Air;
        bool unsupported;
        lock (use.Level.Fluids.TickLock)
            unsupported = waterlogged && !PlacementSurvival.CanSurvive(use.Level.BlockUpdates, replacement, use.Position);

        var drops = unsupported && use.Level.LevelData.GetBooleanRule("block_drops", true)
            ? await BlockLoot.GetDropsAsync(use.Level, use.Position, replacement, null, checkTool: false)
            : [];
        if (!use.TryCommit(() =>
        {
            var flags = waterlogged ? BlockUpdateFlags.All : BlockUpdateFlags.AllImmediate;
            if (!use.Level.BlockUpdates.SetBlock(use.Position, replacement, flags))
                return false;

            if (unsupported)
                use.Level.BlockUpdates.SetBlock(use.Position, BlocksRegistry.Air);

            return true;
        },
            exchangeResult: new ItemStack(filled, 1)))
        {
            return UseResult.Fail;
        }

        if (powder)
            use.Level.BroadcastLevelEvent(2001, use.Position, block.GetHashCode());

        if (unsupported)
        {
            use.Level.BlockDestroyed?.Invoke(use.Position, replacement, true);
            use.Level.BroadcastLevelEvent(2001, use.Position, replacement.GetHashCode());
            use.Level.EmitGameEvent(MobGameEvent.BlockDestroy, use.Center, null, affectedBlock: replacement);
            foreach (var drop in drops)
                use.Level.SpawnItem(use.Center, drop);
        }

        use.Level.PacketBroadcaster.BroadcastToLevelInRange(use.Level, use.Player.Position, new SoundPacket
        {
            SoundLocation = sound,
            SoundPosition = new SoundPosition(use.Player.Position.X, use.Player.Position.Y, use.Player.Position.Z),
            Category = SoundCategory.Players,
            Volume = 1,
            Pitch = 1,
            Seed = Globals.Random.NextInt64()
        }, use.Player.EntityId);
        use.Level.EmitGameEvent(MobGameEvent.FluidPickup, use.Center, use.Player);
        return UseResult.Success;
    }

    private static async ValueTask<UseResult> PourAsync(BlockUse use)
    {
        var level = use.Level;
        var water = use.Item!.Holder.Id == ItemIds.WaterBucket;
        var kind = water ? FluidKind.Water : FluidKind.Lava;
        var source = new FluidState(kind, 8, false);
        var beside = use.Position + use.Face.ToVector();
        var destination = water && use.Block.IsLiquidBlockContainer() ? use.Position : beside;
        IBlock previous;
        IBlockEntity? entity;
        bool waterlog;
        bool evaporates;
        lock (level.Fluids.TickLock)
        {
            previous = level.BlockUpdates.GetBlock(destination);
            if (!Accepts(previous, kind, use.Player.Sneaking))
            {
                destination = beside;
                previous = level.BlockUpdates.GetBlock(destination);
                // Vanilla's retry beside the hit has no hit result: sneaking no longer excludes replacement.
                if (!Accepts(previous, kind, false))
                    return UseResult.Fail;
            }

            if (level.IsOutsideBuildHeight(destination.Y))
                return UseResult.Fail;

            entity = level.GetResidentBlockEntity(destination);
            waterlog = water && previous.IsLiquidBlockContainer();
            evaporates = water && level.Gameplay(LevelEnvironment.WaterEvaporates, destination);
        }

        var destroys = !evaporates && !waterlog && !previous.IsAir && !previous.IsLiquidBlock();
        await using var captured = destroys
            ? await BlockEntityComponents.CaptureAsync(level, destination, null)
            : BlockEntityComponents.None;
        if (!captured.CanBreak)
            return UseResult.Fail;

        var drops = destroys && level.LevelData.GetBooleanRule("block_drops", true)
            ? await BlockLoot.GetDropsAsync(level, destination, previous, null, checkTool: false,
                blockEntityComponents: captured.Components, dynamicDrops: captured.DynamicDrops)
            : [];

        var final = source.CreateLegacyBlock();
        if (waterlog)
            final = previous.GetProperty("waterlogged") is null ? previous : previous.WithProperty("waterlogged", true);
        var placing = new BlockPlaceEventArgs(use.Server, use.Player, final, destination, level, use.Hand)
        {
            Sequence = use.Sequence
        };
        await use.Server.EventDispatcher.ExecuteEventAsync(placing);
        if (placing.IsCancelled)
            return UseResult.Fail;

        var committed = use.TryCommit(() =>
        {
            if (!level.BlockUpdates.GetBlock(destination).IsSameState(previous)
                || !ReferenceEquals(level.GetResidentBlockEntity(destination), entity))
            {
                return false;
            }

            if (evaporates)
                return true;

            if (waterlog)
            {
                // Vanilla's bucket ignores placeLiquid's result, including already-full containers.
                level.Fluids.PlaceLiquid(destination, previous, source);
                return true;
            }

            if (destroys)
            {
                var emptied = FluidState.Of(previous).CreateLegacyBlock();
                if (!level.BlockUpdates.SetBlock(destination, emptied, BlockUpdateFlags.All))
                    return false;

                // Keep the intermediate state until its shape updates run, with the hand already unlocked.
                // The second write still runs under TryCommit's tick lock, before another use can intervene.
                var effects = level.ActiveBlockUse!;
                effects.After(() =>
                {
                    level.BlockDestroyed?.Invoke(destination, previous, true);
                    if (previous.BlockClass() is not ("FireBlock" or "SoulFireBlock"))
                        level.BroadcastLevelEvent(2001, destination, previous.GetHashCode());

                    level.EmitGameEvent(MobGameEvent.BlockDestroy, (VectorD)destination + 0.5,
                        null, affectedBlock: previous);
                    foreach (var drop in drops)
                        level.SpawnItem((VectorD)destination + 0.5, drop);
                });
                effects.After(() => level.BlockUpdates.SetBlock(destination, final, BlockUpdateFlags.AllImmediate));
                return true;
            }

            // Pouring into an existing source succeeds even if setting that identical state makes no change.
            return level.BlockUpdates.SetBlock(destination, final, BlockUpdateFlags.AllImmediate)
                || previous.IsSameState(final);
        }, exchangeResult: use.Player.GameMode == GameMode.Creative
            ? null : new ItemStack(ItemsRegistry.Bucket, 1));
        if (!committed)
            return UseResult.Fail;

        if (evaporates)
        {
            level.Evaporates(use.Player, destination);
            return UseResult.Success;
        }

        BlockLighting.Play(level, destination, water ? "minecraft:item.bucket.empty" : "minecraft:item.bucket.empty_lava",
            except: use.Player.EntityId);
        level.EmitGameEvent(MobGameEvent.FluidPlace, (VectorD)destination + 0.5, use.Player);
        await use.Player.FireProgressAsync("placed_block", item: use.UsedItem, block: final, position: destination);
        return UseResult.Success;
    }

    // Bucket replacement uses BlockState.canBeReplaced(Fluid), broader than item placement's replaceable flag.
    private static bool Accepts(IBlock block, FluidKind fluid, bool sneaking) => block.IsAir
        || !sneaking && (block.CanBeReplaced() || !block.IsSolid()
            || block.IsLiquidBlockContainer() && (block.CanHoldSpecificFluid(fluid)
                || fluid == FluidKind.Water && block.GetProperty("waterlogged") == "true"));

    private static BlockShapeHit? Trace(AbstractLevel level, PlayerBase player, bool sources)
    {
        var start = PlayerBase.EyePosition(player);
        var end = start + (VectorD)player.GetLookDirection() * PlayerBase.BlockInteractionRange(player);
        var terrain = new MobTerrain(level);
        var nearest = terrain.RaycastOutlines(start, end);
        if (!sources)
            return nearest;

        var direction = end - start;
        var distance = nearest is BlockShapeHit outline
            ? ((VectorD)outline.Position + outline.ClickedAt - start).MagnitudeSquared() : double.MaxValue;
        foreach (var cell in MobTerrain.CellsAlong(start, end))
        {
            if (terrain.GetBlock(cell) is not IBlock block)
                break;

            var fluid = FluidState.Of(block);
            if (!fluid.IsSource)
                continue;

            var above = terrain.GetBlock(cell + Vector.Up);
            var height = above is not null && FluidState.Of(above).Type == fluid.Type ? 1 : fluid.OwnHeight;
            var box = new BoundingBox(VectorD.Zero, new VectorD(1, height, 1));
            var clipped = BlockShapeRaycast.Clip([box], cell, VectorD.Zero, start, direction);
            if (clipped is not null)
            {
                var hit = clipped.Value.Hit;
                var candidate = ((VectorD)hit.Position + hit.ClickedAt - start).MagnitudeSquared();
                if (candidate < distance)
                {
                    distance = candidate;
                    nearest = hit;
                }
            }
        }

        return nearest;
    }
}
