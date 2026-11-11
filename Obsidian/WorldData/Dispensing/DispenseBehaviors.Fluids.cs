using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData.Fluids;

namespace Obsidian.WorldData.Dispensing;

internal static partial class DispenseBehaviors
{
    private static void RegisterFluids(Dictionary<int, IDispenseBehavior> behaviors)
    {
        var filled = new FilledBucket();
        int[] buckets =
        [
            ItemIds.WaterBucket, ItemIds.LavaBucket, ItemIds.PowderSnowBucket,
            ItemIds.CodBucket, ItemIds.SalmonBucket, ItemIds.PufferfishBucket,
            ItemIds.TropicalFishBucket, ItemIds.AxolotlBucket, ItemIds.TadpoleBucket
        ];
        foreach (var item in buckets)
        {
            behaviors[item] = filled;
        }

        behaviors[ItemIds.Bucket] = new EmptyBucket();
        behaviors[ItemIds.GlassBottle] = new GlassBottle();
        behaviors[ItemIds.Potion] = new WaterPotion();
        behaviors[ItemIds.Honeycomb] = new Honeycomb();
    }

    internal sealed record BucketContents(FluidKind Fluid, string Sound, EntityType? Entity = null,
        bool PowderSnow = false)
    {
        internal static BucketContents? Of(ItemStack stack) => stack.Holder.Id switch
        {
            ItemIds.WaterBucket => new(FluidKind.Water, "minecraft:item.bucket.empty"),
            ItemIds.LavaBucket => new(FluidKind.Lava, "minecraft:item.bucket.empty_lava"),
            ItemIds.PowderSnowBucket => new(FluidKind.Empty, "minecraft:item.bucket.empty_powder_snow",
                PowderSnow: true),
            ItemIds.CodBucket => Fish(EntityType.Cod),
            ItemIds.SalmonBucket => Fish(EntityType.Salmon),
            ItemIds.PufferfishBucket => Fish(EntityType.Pufferfish),
            ItemIds.TropicalFishBucket => Fish(EntityType.TropicalFish),
            ItemIds.AxolotlBucket => new(FluidKind.Water, "minecraft:item.bucket.empty_axolotl", EntityType.Axolotl),
            ItemIds.TadpoleBucket => new(FluidKind.Water, "minecraft:item.bucket.empty_tadpole", EntityType.Tadpole),
            _ => null
        };

        private static BucketContents Fish(EntityType type) =>
            new(FluidKind.Water, "minecraft:item.bucket.empty_fish", type);
    }

    // Vanilla's nested default dispense also plays its click and smoke before the outer behaviour does.
    private static Dispensed DropWithEffects(DispenseSource source, ItemStack stack)
    {
        var result = Drop.Dispense(source, stack);
        source.Level.BroadcastLevelEvent(result.Event, source.Position, 0);
        source.Level.BroadcastLevelEvent(2000, source.Position, (int)source.Facing);
        return result;
    }

    // addToInventory uses the first empty slot; it doesn't merge into an existing stack of the result.
    private static Dispensed Exchange(DispenseSource source, ItemStack stack, ItemStack result)
    {
        var rest = DispenseSource.Shrink(stack);
        if (rest is null)
            return Dispensed.Kept(result);

        for (var slot = 0; slot < source.Inventory.Size; slot++)
        {
            if (source.Inventory.GetItem(slot) is { Count: > 0, IsAir: false })
                continue;

            source.Inventory.SetItem(slot, result);
            return Dispensed.Kept(rest);
        }

        DropWithEffects(source, result);
        return Dispensed.Kept(rest);
    }

    private sealed class FilledBucket : IDispenseBehavior
    {
        public Dispensed Dispense(DispenseSource source, ItemStack stack)
        {
            var level = source.Level;
            var target = source.Target;
            var prepared = source.Bucket!;
            var previous = prepared.Block;
            var contents = BucketContents.Of(stack)!;
            var accepts = contents.PowderSnow
                ? previous.IsAir || !previous.IsLiquidBlock() && previous.CanBeReplaced()
                : BucketItems.Accepts(previous, contents.Fluid, sneaking: false);
            if (!accepts || level.IsOutsideBuildHeight(target.Y))
                return DropWithEffects(source, stack);

            var evaporates = !contents.PowderSnow && contents.Fluid == FluidKind.Water
                && level.Gameplay(LevelEnvironment.WaterEvaporates, target);
            if (evaporates)
            {
                level.Evaporates(null, target);
            }
            else if (contents.PowderSnow)
            {
                level.BlockUpdates.SetBlock(target, BlocksRegistry.PowderSnow, BlockUpdateFlags.AllImmediate);
            }
            else if (contents.Fluid == FluidKind.Water && previous.IsLiquidBlockContainer())
            {
                level.Fluids.PlaceLiquid(target, previous, new FluidState(FluidKind.Water, 8, false));
            }
            else
            {
                Pour(source, prepared, contents.Fluid);
            }

            if (!evaporates)
            {
                PlayEmptySound(source, contents);
                if (contents.Entity is null)
                    level.EmitGameEvent(MobGameEvent.FluidPlace, Center(target));
            }

            if (prepared.Mob is Mob mob)
            {
                mob.EntityId = Server.GetNextEntityId();
                mob.Position = (VectorD)target + new VectorD(0.5, 0, 0.5);
                mob.Yaw = level.GrowthRandom.NextFloat() * 360;
                if (level.TrySpawnEntity(mob, hostOwned: true))
                    level.EmitGameEvent(MobGameEvent.EntityPlace, Center(target));
            }

            // A successful filled-bucket dispense returns a fresh empty bucket.
            return Dispensed.Kept(new ItemStack(ItemsRegistry.Bucket, 1));
        }

        private static void PlayEmptySound(DispenseSource source, BucketContents contents)
        {
            if (contents.Entity is null)
            {
                BlockLighting.Play(source.Level, source.Target, contents.Sound);
                return;
            }

            var center = Center(source.Target);
            source.Level.PacketBroadcaster.BroadcastToLevelInRange(source.Level, source.Target, new SoundPacket
            {
                SoundLocation = contents.Sound,
                SoundPosition = new SoundPosition(center.X, center.Y, center.Z),
                Category = SoundCategory.Neutral,
                Volume = 1,
                Pitch = 1,
                Seed = Globals.Random.NextInt64()
            });
        }

        private static void Pour(DispenseSource source, BucketPreparation prepared, FluidKind kind)
        {
            var level = source.Level;
            var target = source.Target;
            var final = new FluidState(kind, 8, false).CreateLegacyBlock();
            if (prepared.Destroys)
            {
                var previous = prepared.Block;
                level.BlockUpdates.SetBlock(target, FluidState.Of(previous).CreateLegacyBlock());
                level.ActiveBlockUse!.After(() =>
                {
                    level.BlockDestroyed?.Invoke(target, previous, true);
                    if (previous.BlockClass() is not ("FireBlock" or "SoulFireBlock"))
                        level.BroadcastLevelEvent(2001, target, previous.GetHashCode());

                    level.EmitGameEvent(MobGameEvent.BlockDestroy, Center(target), affectedBlock: previous);
                    foreach (var drop in prepared.Drops)
                        level.PopBlockResource(target, drop);
                });
            }

            level.BlockUpdates.SetBlock(target, final, BlockUpdateFlags.AllImmediate);
        }
    }

    private sealed class EmptyBucket : IDispenseBehavior
    {
        public Dispensed Dispense(DispenseSource source, ItemStack stack)
        {
            var prepared = source.Bucket!;
            var block = prepared.Block;
            if (BucketItems.PickupItem(block) is not Item item)
                return Drop.Dispense(source, stack);

            var level = source.Level;
            var target = source.Target;
            var waterlogged = block.GetProperty("waterlogged") == "true";
            var replacement = waterlogged ? block.WithProperty("waterlogged", false) : BlocksRegistry.Air;
            var flags = waterlogged ? BlockUpdateFlags.All : BlockUpdateFlags.AllImmediate;
            level.BlockUpdates.SetBlock(target, replacement, flags);
            if (prepared.Unsupported)
            {
                level.BlockUpdates.SetBlock(target, BlocksRegistry.Air);
                level.ActiveBlockUse!.After(() =>
                {
                    level.BlockDestroyed?.Invoke(target, replacement, true);
                    level.BroadcastLevelEvent(2001, target, replacement.GetHashCode());
                    level.EmitGameEvent(MobGameEvent.BlockDestroy, Center(target), affectedBlock: replacement);
                    foreach (var drop in prepared.Drops)
                        level.PopBlockResource(target, drop);
                });
            }

            if (block.Is(BlockIds.PowderSnow))
                level.BroadcastLevelEvent(2001, target, block.GetHashCode());

            level.EmitGameEvent(MobGameEvent.FluidPickup, Center(target));
            return Exchange(source, stack, new ItemStack(item, 1));
        }
    }

    private sealed class GlassBottle : IDispenseBehavior
    {
        public Dispensed Dispense(DispenseSource source, ItemStack stack)
        {
            var fluid = FluidState.Of(source.Level.BlockUpdates.GetBlock(source.Target));
            if (!fluid.IsWater)
            {
                var dropped = Drop.Dispense(source, stack);
                return new Dispensed(dropped.Rest, Dispensed.FailEvent);
            }

            source.Level.EmitGameEvent(MobGameEvent.FluidPickup, Center(source.Position));
            var water = new ItemStack(ItemsRegistry.Potion, 1,
                new PotionContentsDataComponent { Potion = Potion.Water });
            return Exchange(source, stack, water);
        }
    }

    private sealed class WaterPotion : IDispenseBehavior
    {
        private static readonly ParticleDefinition splash = new((int)ParticleType.Splash, "minecraft:splash");

        public Dispensed Dispense(DispenseSource source, ItemStack stack)
        {
            var level = source.Level;
            var potion = stack.GetComponent<PotionContentsDataComponent>(DataComponentType.PotionContents);
            var block = level.BlockUpdates.GetBlock(source.Target);
            if (potion?.Potion != Potion.Water || !potion.CustomEffects.IsDefaultOrEmpty
                || !TagsRegistry.Block.ConvertibleToMud.Entries.Contains(block.RegistryId))
            {
                return DropWithEffects(source, stack);
            }

            for (var index = 0; index < 5; index++)
            {
                var at = (VectorD)source.Position
                    + new VectorD(level.GrowthRandom.NextDouble(), 1, level.GrowthRandom.NextDouble());
                level.SpawnParticles(splash, at);
            }

            BlockLighting.Play(level, source.Position, "minecraft:item.bottle.empty");
            level.EmitGameEvent(MobGameEvent.FluidPlace, Center(source.Position));
            level.BlockUpdates.SetBlock(source.Target, BlocksRegistry.Mud);
            return Dispensed.Kept(new ItemStack(ItemsRegistry.GlassBottle, 1));
        }
    }

    private sealed class Honeycomb : IDispenseBehavior
    {
        public Dispensed Dispense(DispenseSource source, ItemStack stack)
        {
            var level = source.Level;
            if (CopperWaxing.Waxed(level.BlockUpdates.GetBlock(source.Target)) is not IBlock waxed)
                return Drop.Dispense(source, stack);

            level.BlockUpdates.SetBlock(source.Target, waxed);
            level.BroadcastLevelEvent(3003, source.Target, 0);
            BlockLighting.Play(level, source.Target, "minecraft:item.honeycomb.wax_on");
            return Dispensed.Spent(stack);
        }
    }
}
