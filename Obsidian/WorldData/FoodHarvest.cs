using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.Loot;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData;

/// <summary>Atomic rewards and tool costs shared by food and harvest uses.</summary>
internal static class FoodHarvest
{
    /// <summary>Evaluates 26.3's block-interaction loot inside a commit; drops publish after inventories unlock.</summary>
    internal static void QueueLoot(BlockUse use, string identifier, BlockFace? pumpkinFace = null)
    {
        var resolver = PluginLootTables.CaptureResolver();
        var table = resolver("minecraft:" + identifier) ?? LootTable.Empty;
        var random = table.CreateRandom(0, BlockLoot.SequencesFor(use.Level));
        List<ItemStack> drops;
        lock (random)
        {
            var context = new LootContext
            {
                Random = random,
                Tool = use.Item,
                BlockState = use.Block,
                BlockProperties = use.Block.GetProperties(),
                ThisEntity = use.Player,
                Origin = use.Center,
                ResolveTable = resolver
            };
            drops = table.GetRandomItems(context).ToList();
        }

        foreach (var stack in drops)
        {
            // Reserve the location draws now, before hive aggression can draw from the same stream.
            // TrySpawnEntity registers the entity here and defers its spawn packets through ActiveBlockUse.
            if (pumpkinFace is BlockFace face)
                DropPumpkinSeeds(use, face, stack);
            else
                use.Level.PopHarvestResource(use.Position, stack);
        }
    }

    internal static void DropCandle(BlockUse use)
    {
        var name = use.Block.UnlocalizedName[..^"_cake".Length];
        use.Level.PopHarvestResource(use.Position, new ItemStack(ItemsRegistry.Get(name), 1));
    }

    private static void DropPumpkinSeeds(BlockUse use, BlockFace face, ItemStack stack)
    {
        // PumpkinBlock's drop is placed outside its horizontal carving face, with a small outward push.
        var direction = face.ToVector();
        var point = (VectorD)use.Position + new VectorD(
            0.5 + direction.X * 0.65,
            0.1,
            0.5 + direction.Z * 0.65);
        var random = use.Level.GrowthRandom;
        var motion = new VectorD(
            direction.X * 0.05 + random.NextDouble() * 0.02,
            0.05,
            direction.Z * 0.05 + random.NextDouble() * 0.02);
        use.Level.DropItemAt(point, stack, motion);
    }

    /// <summary>One point of shear wear, inside TryCommit so a stale or busy hand cannot receive a reward.</summary>
    internal static void WearShears(BlockUse use)
    {
        var item = use.Item!;
        if (use.Player.GameMode == GameMode.Creative)
            return;

        var previous = item.Damage;
        var broke = CombatItems.HurtItem(item, 1);
        if (!broke && item.Damage == previous)
            return;

        if (broke)
        {
            if (item.Count == 0)
                use.Player.Inventory.RemoveItem(use.HandSlot);

            use.Level.ActiveBlockUse!.After(() =>
            {
                use.Player.Statistics.Increment(item.Holder.UnlocalizedName, type: "minecraft:broken");
                use.Level.PacketBroadcaster.BroadcastToLevelInRange(use.Level, use.Player.Position,
                    new EntityEventPacket
                    {
                        EntityId = use.Player.EntityId,
                        Event = use.Hand == InteractionHand.OffHand ? (byte)48 : (byte)47
                    });
            });
        }

        use.ResyncHand = true;
        use.ChangedInventorySlots.Add(use.HandSlot);
    }

    internal static async ValueTask<UseResult> TrimAsync(BlockUse use)
    {
        if (use.Block.RegistryId is not (BlockIds.CaveVines or BlockIds.TwistingVines
            or BlockIds.WeepingVines or BlockIds.Kelp) || use.Block.GetProperty("age") == "25")
        {
            return UseResult.Pass;
        }

        var before = use.Player.MatchProgress("item_used_on_block",
            item: use.UsedItem, block: use.Block, position: use.Position);
        var next = use.Block.WithProperty("age", 25);
        if (!use.TryCommit(() =>
        {
            if (!use.Level.BlockUpdates.SetBlock(use.Position, next, BlockUpdateFlags.All))
                return false;

            WearShears(use);
            return true;
        }))
        {
            return UseResult.Fail;
        }

        await use.Player.GrantProgressAsync(before);
        BlockLighting.Play(use.Level, use.Position, "minecraft:block.growing_plant.crop", except: use.Player.EntityId);
        use.Level.EmitGameEvent(MobGameEvent.BlockChange, use.Center, use.Player, affectedBlock: next);
        return UseResult.Success;
    }

    internal static void PlayAtPlayer(BlockUse use, string sound, VectorD position, int excluded)
    {
        use.Level.PacketBroadcaster.BroadcastToLevelInRange(use.Level, position, new SoundPacket
        {
            SoundLocation = sound,
            SoundPosition = new SoundPosition(position.X, position.Y, position.Z),
            Category = SoundCategory.Blocks,
            Volume = 1,
            Pitch = 1,
            Seed = Globals.Random.NextInt64()
        }, excluded);
    }

    /// <summary>Emergency harvest release ignores weather, elapsed time and an obstructed exit.</summary>
    internal static void ReleaseBees(BlockUse use)
    {
        if (use.BlockEntity is not DataBlockEntity hive || hive.SnapshotTag<NbtList>("bees") is not NbtList occupants
            || occupants.Count == 0)
        {
            return;
        }

        var level = use.Level;
        var center = use.Center;
        var nearby = level.GetEntitiesInRange(center, 12).OfType<Bee>().Where(bee =>
            bee.AttackTarget is null && Math.Abs(bee.Position.X - center.X) < 8.5
            && Math.Abs(bee.Position.Y - center.Y) < 6.5 && Math.Abs(bee.Position.Z - center.Z) < 8.5).ToArray();
        var players = level.Players.Values.Where(player => player.GameMode != GameMode.Spectator
            && Math.Abs(player.Position.X - center.X) < 8.5
            && Math.Abs(player.Position.Y - center.Y) < 6.5
            && Math.Abs(player.Position.Z - center.Z) < 8.5).ToArray();
        if (players.Length > 0)
        {
            foreach (var bee in nearby)
            {
                var target = players[level.GrowthRandom.NextInt(players.Length)];
                level.ActiveBlockUse!.After(() => bee.TargetHarvester(target));
            }
        }

        var face = FeatureHelpers.ParseFace(use.Block.GetProperty("facing"));
        var direction = face.ToVector();
        var exit = use.Position + direction;
        var blocked = Entities.AI.BlockCollisionShapes.Get(level, exit,
            level.GameEventBlock(exit) ?? BlocksRegistry.Air).Count > 0;
        var retained = new NbtList(NbtTagType.Compound, "bees");
        foreach (var occupant in occupants.OfType<NbtCompound>())
        {
            if (!occupant.TryGetTag<NbtCompound>("entity_data", out var saved))
                continue;

            saved.Set(EntityNbt.DoubleList("Pos", center));
            if (EntityNbt.Load(saved, level) is not Bee bee)
                continue;

            var distance = blocked ? 0 : 0.55 + bee.Dimension.Width / 2;
            bee.Position = (VectorD)use.Position + new VectorD(
                0.5 + direction.X * distance,
                0.5 - bee.Dimension.Height / 2,
                0.5 + direction.Z * distance);
            bee.HivePosition = use.Position;
            var elapsed = Math.Max(0, occupant.GetInt("ticks_in_hive"));
            bee.Age = bee.Age < 0 ? Math.Min(0, bee.Age + elapsed) : Math.Max(0, bee.Age - elapsed);
            if (!level.TrySpawnEntity(bee, hostOwned: true))
            {
                retained.Add(occupant);
                continue;
            }

            level.ActiveBlockUse!.After(() =>
            {
                if ((bee.Position - use.Player.Position).MagnitudeSquared() <= 16)
                    bee.TargetHarvester(use.Player);

                BlockLighting.Play(level, use.Position, "minecraft:block.beehive.exit");
                level.EmitGameEvent(MobGameEvent.BlockChange, center, bee);
            });
        }

        hive.Set(retained);
    }
}

public abstract partial class AbstractLevel
{
    // Block.popResource consumes three level-random location draws. The item's own motion random must not
    // advance that stream: the harvest sound's pitch reads the level random immediately after these drops.
    internal void PopHarvestResource(Vector position, ItemStack stack)
    {
        lock (this.Fluids.TickLock)
        {
            var random = this.GrowthRandom;
            var x = position.X + 0.5 + (random.NextDouble() * 0.5 - 0.25);
            var y = position.Y + 0.5 + (random.NextDouble() * 0.5 - 0.25) - ItemEntityWidth / 2;
            var z = position.Z + 0.5 + (random.NextDouble() * 0.5 - 0.25);
            if (!this.LevelData.GetBooleanRule("block_drops"))
                return;

            var motion = new VectorD(
                Globals.Random.NextDouble() * 0.2 - 0.1,
                0.2,
                Globals.Random.NextDouble() * 0.2 - 0.1);
            this.DropItemAt(new VectorD(x, y, z), stack, motion);
        }
    }
}
