using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Packets.Play.Serverbound;

namespace Obsidian.WorldData.Dispensing;

internal static partial class DispenseBehaviors
{
    private static void RegisterBlocks(Dictionary<int, IDispenseBehavior> behaviors)
    {
        behaviors[ItemIds.BoneMeal] = new BoneMeal();
        behaviors[ItemIds.FlintAndSteel] = new FlintAndSteel();
        behaviors[ItemIds.Tnt] = new Tnt();
        behaviors[ItemIds.CarvedPumpkin] = new CarvedPumpkin();
        behaviors[ItemIds.WitherSkeletonSkull] = new WitherSkull();
        behaviors[ItemIds.Glowstone] = new Glowstone();

        var shulkerBox = new ShulkerBox();
        foreach (var item in ItemsRegistry.All)
        {
            if (item.UnlocalizedName.EndsWith("shulker_box", StringComparison.Ordinal))
                behaviors[item.Id] = shulkerBox;
        }
    }

    /// <summary>
    /// Vanilla's <c>ItemStack.hurtAndBreak</c> with no one holding the tool: a point of damage, which Unbreaking may
    /// spare, and a tool worn out breaks.
    /// </summary>
    /// <returns>The worn stack, or null when it broke.</returns>
    private static ItemStack? Wear(ItemStack stack)
    {
        if (stack.Unbreakable || !ItemDamage.ShouldDamage(stack))
            return stack;

        var worn = new ItemStack(stack, stack.Count);
        var damage = ComponentBuilder.Damage with { Value = stack.Damage + 1 };
        worn[DataComponentType.Damage] = damage;

        var maxDamage = worn.GetComponent<SimpleDataComponent<int>>(DataComponentType.MaxDamage)?.Value
            ?? worn.Holder.MaxDamage;
        return damage.Value >= maxDamage ? DispenseSource.Shrink(worn) : worn;
    }

    private static VectorD Center(Vector position) => (VectorD)position + new VectorD(0.5, 0.5, 0.5);

    /// <summary>
    /// Vanilla's bone meal behaviour: grows the block in front like a player's bone meal, using one whether it grows
    /// or not, and fails on anything that doesn't take bone meal.
    /// </summary>
    private sealed class BoneMeal : IDispenseBehavior
    {
        public Dispensed Dispense(DispenseSource source, ItemStack stack)
        {
            var level = source.Level;
            var target = source.Target;
            bool used;

            // Under the lock players' block writes take, like a player's bone meal.
            lock (level.Fluids.TickLock)
            {
                var block = level.BlockUpdates.GetBlock(target);
                used = Growth.IsValidBonemealTarget(level, target, block, level.GrowthRandom);
                if (used)
                {
                    var effects = level.ActiveBlockUse
                        ?? throw new InvalidOperationException("Dispenser growth requires a state commit.");
                    effects.After(() => Growth.ApplyBonemeal(level, target, block, level.GrowthRandom));
                }
            }

            if (!used)
                return Dispensed.Failed(stack);

            level.BroadcastLevelEvent(Growth.BoneMealEvent, target, Growth.BoneMealParticles);
            return Dispensed.Spent(stack);
        }
    }

    /// <summary>
    /// Vanilla's <c>FlintAndSteelDispenseItemBehavior</c>: sets fire in front, lights the campfire or candles there,
    /// or primes the TNT there, and wears the flint and steel for it. Facing anything else it still wears and
    /// succeeds, as vanilla's does.
    /// </summary>
    private sealed class FlintAndSteel : IDispenseBehavior
    {
        public Dispensed Dispense(DispenseSource source, ItemStack stack)
        {
            var level = source.Level;
            var updates = level.BlockUpdates;
            var target = source.Target;
            var success = true;

            lock (level.Fluids.TickLock)
            {
                var block = updates.GetBlock(target);
                if (FireBlockBehavior.CanBePlaced(updates, target))
                {
                    updates.SetBlock(target, FireBlockBehavior.StateFor(updates, target));
                    level.EmitGameEvent(MobGameEvent.BlockPlace, Center(target));
                }
                else if (BlockLighting.CanLight(block))
                {
                    var lit = block.WithProperty("lit", true);
                    updates.SetBlock(target, lit);
                    level.EmitGameEvent(MobGameEvent.BlockChange, Center(target), affectedBlock: lit);
                }
                else if (block.Is(BlockIds.Tnt))
                {
                    success = PrimeTnt(level, target);
                }
            }

            var rest = success ? Wear(stack) : stack;
            return new Dispensed(rest, success ? Dispensed.DispenseEvent : Dispensed.FailEvent);
        }

        // Vanilla's TntBlock.prime: the primed TNT with its sound and game event, then the block goes.
        private static bool PrimeTnt(AbstractLevel level, Vector position)
        {
            if (PrimedTnt.Prime(level, position, owner: null, random: level.GrowthRandom) is null)
                return false;

            level.EmitGameEvent(MobGameEvent.PrimeFuse, Center(position));
            level.BlockUpdates.RemoveBlock(position);
            return true;
        }
    }

    /// <summary>
    /// Vanilla's TNT behaviour: primed TNT in front, while the <c>tnt_explodes</c> rule lets TNT explode.
    /// </summary>
    private sealed class Tnt : IDispenseBehavior
    {
        public Dispensed Dispense(DispenseSource source, ItemStack stack)
        {
            var level = source.Level;
            var target = source.Target;
            if (PrimedTnt.Prime(level, target, owner: null, random: level.GrowthRandom) is null)
                return Dispensed.Failed(stack);

            level.EmitGameEvent(MobGameEvent.EntityPlace, Center(target));
            return Dispensed.Spent(stack);
        }
    }

    /// <summary>
    /// Vanilla's <c>ShulkerBoxDispenseBehavior</c>: places the box in front, facing the dispenser's way, or up when
    /// something is under where it goes, with its contents and name.
    /// </summary>
    private sealed class ShulkerBox : IDispenseBehavior
    {
        public Dispensed Dispense(DispenseSource source, ItemStack stack)
        {
            var level = source.Level;
            var target = source.Target;
            if (!BlocksRegistry.TryGetBlock(stack.Holder, out var box))
                return Dispensed.Failed(stack);

            var below = level.BlockUpdates.GetBlock(target + BlockFace.Down.ToVector());
            var facing = below.IsAir ? source.Facing : BlockFace.Up;
            var state = box.WithProperty("facing", facing.ToString().ToLowerInvariant());

            // Vanilla's BlockItem.place: a block it can replace, set with UPDATE_ALL_IMMEDIATE.
            var placed = source.Placement is not null
                && level.PlaceDispensedBlock(target, state, source.Placement);
            if (!placed)
                return Dispensed.Failed(stack);

            BlockSoundPlayback.Play(level, state, target, breaking: false, actor: -1);
            level.EmitGameEvent(MobGameEvent.BlockPlace, Center(target), affectedBlock: state);
            return Dispensed.Spent(stack);
        }
    }

    /// <summary>
    /// Vanilla's carved pumpkin behaviour: placed in front where it completes a golem, which is then built; otherwise
    /// put on the head of a mob in front, or kept.
    /// </summary>
    /// <remarks>
    /// Obsidian builds snow golems only, standing upright, so a pumpkin is placed only on two snow blocks.
    /// </remarks>
    private sealed class CarvedPumpkin : IDispenseBehavior
    {
        public Dispensed Dispense(DispenseSource source, ItemStack stack)
        {
            var level = source.Level;
            var target = source.Target;
            if (level.BlockUpdates.GetBlock(target).IsAir && level.CanBuildSnowGolem(target))
            {
                var pumpkin = BlocksRegistry.CarvedPumpkin;
                level.BlockUpdates.SetBlock(target, pumpkin);
                level.BuildDispensedSnowGolem(target);
                level.EmitGameEvent(MobGameEvent.BlockPlace, Center(target), affectedBlock: pumpkin);
                return Dispensed.Spent(stack);
            }

            return Equip(source, stack);
        }
    }

    /// <summary>
    /// Vanilla's wither skeleton skull behaviour: placed in front, facing away from the dispenser, where it can
    /// complete a wither, which is then built; otherwise put on the head of a mob in front, or kept.
    /// </summary>
    private sealed class WitherSkull : IDispenseBehavior
    {
        public Dispensed Dispense(DispenseSource source, ItemStack stack)
        {
            var level = source.Level;
            var target = source.Target;
            if (!level.BlockUpdates.GetBlock(target).IsAir || !level.CanBuildWither(target))
                return Equip(source, stack);

            var skull = BlocksRegistry.WitherSkeletonSkull.WithProperty("rotation", SkullRotation(source.Facing));
            level.BlockUpdates.SetBlock(target, skull);
            level.EmitGameEvent(MobGameEvent.BlockPlace, Center(target), affectedBlock: skull);
            level.BuildDispensedWither(target);
            return Dispensed.Spent(stack);
        }

        // Vanilla's direction overload maps horizontal facing to sixteenths of a turn, and vertical facing to 0.
        private static int SkullRotation(BlockFace facing)
        {
            var yaw = facing switch
            {
                BlockFace.South => 0,
                BlockFace.West => 90,
                BlockFace.North => 180,
                BlockFace.East => 270,
                _ => 0
            };

            return (int)Math.Floor(yaw / 22.5 + 0.5) & 15;
        }
    }

    /// <summary>
    /// Vanilla's glowstone behaviour: charges the respawn anchor in front, fails on a full one, and is dropped in
    /// front of anything else.
    /// </summary>
    private sealed class Glowstone : IDispenseBehavior
    {
        // Vanilla's RespawnAnchorBlock.MAX_CHARGES.
        private const int MaxCharges = 4;

        public Dispensed Dispense(DispenseSource source, ItemStack stack)
        {
            var level = source.Level;
            var target = source.Target;
            bool anchor;
            IBlock? charged = null;

            lock (level.Fluids.TickLock)
            {
                var block = level.BlockUpdates.GetBlock(target);
                anchor = block.Is(BlockIds.RespawnAnchor);
                var charges = int.TryParse(block.GetProperty("charges"), out var parsed) ? parsed : 0;
                if (anchor && charges < MaxCharges)
                {
                    charged = block.WithProperty("charges", charges + 1);
                    level.BlockUpdates.SetBlock(target, charged);
                }
            }

            if (!anchor)
                return Drop.Dispense(source, stack);

            if (charged is null)
                return Dispensed.Failed(stack);

            // Vanilla's RespawnAnchorBlock.charge: the change's game event, then the charge sound.
            var center = Center(target);
            level.EmitGameEvent(MobGameEvent.BlockChange, center, affectedBlock: charged);
            level.PacketBroadcaster.BroadcastToLevelInRange(level, target, new SoundPacket
            {
                SoundLocation = "minecraft:block.respawn_anchor.charge",
                SoundPosition = new SoundPosition(center.X, center.Y, center.Z),
                Category = SoundCategory.Blocks,
                Volume = 1,
                Pitch = 1,
                Seed = Globals.Random.NextInt64()
            });

            return Dispensed.Spent(stack);
        }
    }
}
