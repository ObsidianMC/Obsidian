using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.API.Loot;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Registries;
using Obsidian.WorldData.Fluids;

namespace Obsidian.WorldData;

/// <summary>
/// A brush's stroke on the block a player looks at, and suspicious sand's and gravel's ticks: vanilla's
/// <c>BrushItem.onUseTick</c> stroke, <c>BrushableBlockEntity</c> and <c>BrushableBlock.tick</c>.
/// </summary>
internal static class Brushing
{
    private const string GenericSound = "minecraft:item.brush.brushing.generic";

    // Vanilla's level event for a finished brushable block, which plays its completion sound and particles.
    private const int CompletedEvent = 3008;

    // Vanilla's level event for a block broken where a falling block landed.
    private const int BrokenAfterFallEvent = 2001;

    // Vanilla's item entity size, which places the uncovered item in the cell in front of the brushed face.
    private const double ItemWidth = 0.25;
    private const double ItemHeight = 0.25;

    // Vanilla's Blocks registration of its two BrushableBlocks: what each becomes and how brushing it sounds.
    private sealed record Brushable(IBlock TurnsInto, string Sound);

    private static Brushable? BrushableOf(IBlock block) => block.RegistryId switch
    {
        BlockIds.SuspiciousSand => new(BlocksRegistry.Sand, "minecraft:item.brush.brushing.sand"),
        BlockIds.SuspiciousGravel => new(BlocksRegistry.Gravel, "minecraft:item.brush.brushing.gravel"),
        _ => null
    };

    /// <summary>A loot table rolled ahead of a stroke, outside the level's locks.</summary>
    private sealed record Rolled(string Table, long Seed, ItemStack? Item);

    /// <summary>What a stroke does outside the level's locks once it has changed the world.</summary>
    private sealed class Stroke
    {
        public required VectorD SoundAt { get; init; }

        public required string Sound { get; init; }

        public string? LootTable { get; set; }

        public (Vector Position, int State)? Completed { get; set; }

        public (VectorD Position, ItemStack Item)? Drop { get; set; }

        public BrushableBlockEntity? Updated { get; set; }

        public bool Worn { get; set; }

        public bool Broke { get; set; }
    }

    /// <summary>
    /// One stroke at <paramref name="hit"/>: the brushing sound, and on suspicious sand or gravel a counted stroke,
    /// which rolls its loot the first time, steps its <c>dusted</c> state, and with the last stroke turns the block
    /// into sand or gravel, drops its item in front of the brushed face and wears the brush.
    /// </summary>
    /// <remarks>
    /// The block, its block entity and the brush change together under the level's tick lock, with the player's
    /// inventory tried, not waited for: a busy inventory, a brush no longer in its slot, or a player who can't act
    /// (gone, dead, elsewhere or being teleported) skips the stroke without changing anything, and the next one ten
    /// ticks later tries again. Loot is rolled before the locks; a table changed meanwhile skips the stroke too.
    /// Sounds, the item, the level event, the slot and the triggers go out once the locks are released.
    /// </remarks>
    internal static async ValueTask StrokeAsync(AbstractLevel level, PlayerBase player, ItemStack brush, int slot,
        BlockShapeHit hit)
    {
        var rolled = Roll(level, level.GetResidentBlockEntity(hit.Position) as BrushableBlockEntity, player, brush);

        Stroke? stroke = null;
        lock (level.Fluids.TickLock)
        {
            if (!CanAct(player, level) || !player.Inventory.TryLock(out var handLock))
                return;

            BlockUseEffects? effects = null;
            using (handLock)
            {
                var held = slot == 45 || slot == player.CurrentHeldItemSlot;
                if (!held || !ReferenceEquals(player.Inventory.GetItem(slot), brush))
                    return;

                using (effects = new BlockUseEffects(level))
                {
                    stroke = Apply(level, player, brush, slot, hit, rolled);
                }
            }

            effects?.Publish();
        }

        if (stroke is not null)
            await PublishAsync(level, player, slot, stroke);
    }

    // The player can still change this level.
    private static bool CanAct(PlayerBase player, AbstractLevel level) =>
        player.Connected && player.Alive && !player.Respawning && !player.HasPendingTeleport
        && ReferenceEquals(player.Level, level);

    // Vanilla's unpackLootTable roll: the table with the block entity's seed (or the level's sequence for no seed),
    // from the block's center, for the player with the brush. Only the first item is kept, as vanilla keeps it.
    private static Rolled? Roll(AbstractLevel level, BrushableBlockEntity? brushable, PlayerBase player,
        ItemStack brush)
    {
        if (brushable?.LootTable is not string name)
            return null;

        var seed = brushable.LootTableSeed;
        var resolver = PluginLootTables.CaptureResolver();
        var table = resolver(name) ?? LootTable.Empty;
        var random = table.CreateRandom(seed, BlockLoot.SequencesFor(level));
        var luck = player.HasAttribute("minecraft:luck") ? player.GetAttributeValue("minecraft:luck") : 0;
        List<ItemStack> items;
        lock (random)
        {
            items = table.GetRandomItems(new LootContext
            {
                Random = random,
                Origin = (VectorD)brushable.BlockPosition + new VectorD(0.5, 0.5, 0.5),
                ThisEntity = player,
                Tool = brush,
                Luck = luck,
                ResolveTable = resolver
            });
        }

        return new Rolled(name, seed, items.Count > 0 ? items[0] : null);
    }

    // The stroke's changes, under the tick lock and the player's inventory lock, with the level's block
    // notifications held until both are released.
    private static Stroke? Apply(AbstractLevel level, PlayerBase player, ItemStack brush, int slot,
        BlockShapeHit hit, Rolled? rolled)
    {
        var position = hit.Position;
        var state = level.BlockUpdates.GetBlock(position);
        var kind = BrushableOf(state);
        var brushable = level.GetResidentBlockEntity(position) as BrushableBlockEntity;

        // The table was rolled for what the block entity held when the stroke began.
        var pending = brushable?.LootTable;
        if (pending is not null && (rolled is null || rolled.Table != pending
            || rolled.Seed != brushable!.LootTableSeed))
        {
            return null;
        }

        var stroke = new Stroke
        {
            SoundAt = (VectorD)position + new VectorD(0.5, 0.5, 0.5),
            Sound = kind?.Sound ?? GenericSound
        };

        if (brushable is null)
            return stroke;

        var counted = brushable.Brush(level.LevelData.GameTime, hit.Face);
        if (!counted.Counted)
            return stroke;

        if (pending is not null && brushable.TakeLoot(rolled!.Table, rolled.Seed, rolled.Item))
            stroke.LootTable = pending;

        if (counted.Finished)
        {
            Complete(level, player, brush, slot, position, state, kind, brushable, stroke);
            return stroke;
        }

        level.BlockUpdates.Ticks.Schedule(position, state.UnlocalizedName, 2);
        if (counted.Before != counted.After)
        {
            level.BlockUpdates.SetBlock(position, state.WithProperty("dusted", counted.After));
            stroke.Updated = brushable;
        }

        return stroke;
    }

    // Vanilla's brushingCompleted and dropContent, then the brush's wear: the item pops out of the brushed face (the
    // top without one), the level event plays, the block becomes sand or gravel, and the brush takes one damage.
    private static void Complete(AbstractLevel level, PlayerBase player, ItemStack brush, int slot, Vector position,
        IBlock state, Brushable? kind, BrushableBlockEntity brushable, Stroke stroke)
    {
        var face = brushable.HitDirection ?? BlockFace.Up;
        if (brushable.TakeItem() is { IsAir: false, Count: > 0 } item)
        {
            // Vanilla splits off 10 to 30 of the item with a level draw; the rest is lost.
            var count = Math.Min(item.Count, level.GrowthRandom.NextInt(21) + 10);
            var cell = (VectorD)(position + face.ToVector());
            var at = cell + new VectorD(0.5 * (1 - ItemWidth) + ItemWidth / 2, 0.5 + ItemHeight / 2,
                0.5 * (1 - ItemWidth) + ItemWidth / 2);
            stroke.Drop = (at, new ItemStack(item, count));
        }

        stroke.Completed = (position, state.GetHashCode());
        level.BlockUpdates.SetBlock(position, kind?.TurnsInto ?? BlocksRegistry.Air);

        // Vanilla's hurtAndBreak spares a creative player's brush.
        if (player.GameMode == GameMode.Creative)
            return;

        stroke.Worn = true;
        stroke.Broke = CombatItems.HurtItem(brush, 1, false);
        if (stroke.Broke && brush.Count == 0)
            player.Inventory.RemoveItem(slot);
    }

    private static async ValueTask PublishAsync(AbstractLevel level, PlayerBase player, int slot, Stroke stroke)
    {
        // Vanilla's level.playSound(player, ...): the brushing player's client plays its own.
        var sound = new SoundPacket
        {
            SoundLocation = stroke.Sound,
            SoundPosition = new SoundPosition(stroke.SoundAt.X, stroke.SoundAt.Y, stroke.SoundAt.Z),
            Category = SoundCategory.Blocks,
            Volume = 1,
            Pitch = 1,
            Seed = Random.Shared.NextInt64()
        };
        player.PacketBroadcaster.QueuePacketToLevelInRange(level, stroke.SoundAt, sound, player.EntityId);

        stroke.Updated?.SendUpdate();

        if (stroke.LootTable is string table)
            await player.FireProgressAsync("player_generates_container_loot", lootTable: table);

        if (stroke.Drop is var (at, item))
            level.DropItemAt(at, item, VectorD.Zero);

        if (stroke.Completed is var (position, state))
        {
            var completed = new LevelEventPacket(CompletedEvent, position, state);
            player.PacketBroadcaster.QueuePacketToLevelInRange(level, (VectorD)position, completed);
        }

        if (!stroke.Worn)
            return;

        await player.SyncInventorySlotAsync(slot);
        if (!stroke.Broke)
            return;

        player.Statistics.Increment(ItemsRegistry.Brush.UnlocalizedName, type: "minecraft:broken");
        var breakEvent = new EntityEventPacket
        {
            EntityId = player.EntityId,
            Event = (byte)(slot == 45 ? 48 : 47)
        };
        player.PacketBroadcaster.QueuePacketToLevel(level, breakEvent);
    }

    /// <summary>
    /// Vanilla's <c>BrushableBlock.tick</c>: brushing that has stopped comes undone, then a block with nothing below
    /// it falls, as a falling block that breaks where it lands instead of being placed.
    /// </summary>
    internal sealed class BlockBehavior : IScheduledTickBehavior
    {
        public void Tick(IBlockLevel blocks, Vector position, IBlock block, IRandomSource random)
        {
            if (blocks.Level is not AbstractLevel level)
                return;

            if (level.GetResidentBlockEntity(position) is BrushableBlockEntity brushable)
                CheckReset(level, position, brushable);

            var below = level.BlockUpdates.GetBlock(position + Vector.Down);
            if (!below.IsFreeForFallingBlock() || position.Y < level.MinY)
                return;

            // FallingBlockEntity.fall keeps the state it falls with; disableDrop makes it break when it lands.
            var state = level.BlockUpdates.GetBlock(position);
            level.BlockUpdates.SetBlock(position, FluidState.Of(state).CreateLegacyBlock());
            var falling = (FallingBlock)level.SpawnFallingBlock((VectorD)position, state);
            falling.CancelDrop = true;
        }

        // Vanilla's checkReset, with the dusted state written and the block checking again while strokes remain.
        private static void CheckReset(AbstractLevel level, Vector position, BrushableBlockEntity brushable)
        {
            var reset = brushable.CheckReset(level.LevelData.GameTime);
            if (reset.Before != reset.After)
            {
                var state = level.BlockUpdates.GetBlock(position);
                level.BlockUpdates.SetBlock(position, state.WithProperty("dusted", reset.After));
                brushable.SendUpdate();
            }

            if (reset.Pending)
            {
                var name = level.BlockUpdates.GetBlock(position).UnlocalizedName;
                level.BlockUpdates.Ticks.Schedule(position, name, 2);
            }
        }
    }

    /// <summary>
    /// Vanilla's <c>BrushableBlock.onBrokenAfterFall</c>: where suspicious sand or gravel broke on landing, the
    /// block's break level event plays and the block is destroyed as a game event.
    /// </summary>
    internal static void BrokenAfterFall(AbstractLevel level, FallingBlock falling)
    {
        var box = falling.BoundingBox;
        var center = (box.Min + box.Max) * 0.5;
        var cell = (Vector)center.Floor();
        var packet = new LevelEventPacket(BrokenAfterFallEvent, cell, falling.Block.GetHashCode());
        falling.PacketBroadcaster.QueuePacketToLevelInRange(level, center, packet);
        level.EmitGameEvent(MobGameEvent.BlockDestroy, center, falling);
    }

    /// <summary>Whether <paramref name="block"/> is suspicious sand or gravel.</summary>
    internal static bool IsBrushable(IBlock block) => BrushableOf(block) is not null;
}
