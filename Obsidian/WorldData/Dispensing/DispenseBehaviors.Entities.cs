using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Entities;
using Obsidian.Entities.Factories;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.WorldData.Dispensing;

internal static partial class DispenseBehaviors
{
    /// <summary>
    /// Vanilla's <c>EquipmentDispenseItemBehavior</c>: puts equipment on a mob in front, or drops it.
    /// </summary>
    private static IDispenseBehavior Equipment { get; } = new EquipmentBehavior();

    /// <summary>Vanilla's <c>SpawnEggItemBehavior</c>: the egg's mob in front.</summary>
    private static IDispenseBehavior SpawnEgg { get; } = new SpawnEggBehavior();

    /// <summary>
    /// Vanilla's <c>BoatDispenseItemBehavior</c> and <c>MinecartDispenseItemBehavior</c> (see
    /// <see cref="VehicleItems.Dispense"/>), dropping the item where no vehicle can go.
    /// </summary>
    private static IDispenseBehavior Vehicle { get; } = new VehicleBehavior();

    private static void RegisterEntities(Dictionary<int, IDispenseBehavior> behaviors)
    {
        behaviors[ItemIds.Shears] = new Shears();
    }

    /// <summary>
    /// Vanilla's <c>EquipmentDispenseItemBehavior.dispenseEquipment</c> where nothing else is done instead: the
    /// equipment goes on a mob in front, or the dispenser fails and keeps it.
    /// </summary>
    private static Dispensed Equip(DispenseSource source, ItemStack stack) =>
        TryEquip(source, stack) ? Dispensed.Spent(stack) : Dispensed.Failed(stack);

    /// <summary>
    /// Vanilla's <c>dispenseEquipment</c>: one of the stack goes into its slot on the first mob in front that can
    /// wear it there, which then always drops it and never despawns.
    /// </summary>
    /// <returns>Whether a mob put it on.</returns>
    private static bool TryEquip(DispenseSource source, ItemStack stack)
    {
        if (Wearable.Of(stack) is not { Dispensable: true } wearable)
            return false;

        var level = source.Level;
        var min = (VectorD)source.Target;
        var mob = level.EntitiesIn(min, min + new VectorD(1, 1, 1))
            .OfType<Mob>()
            .FirstOrDefault(candidate => CanEquip(candidate, wearable));
        if (mob is null)
            return false;

        var slot = wearable.Slot;
        mob.SetEquipment(slot, new ItemStack(stack, 1));
        mob.GuaranteeDrop(slot);
        mob.PersistenceRequired = true;

        // Vanilla's LivingEntity.onEquipItem: the equip sound, unless the mob is silent, and the game event.
        if (!mob.Silent)
            PlayEquipSound(mob, wearable.EquipSound);

        level.EmitGameEvent(MobGameEvent.Equip, mob.Position, mob);
        return true;
    }

    // Vanilla's LivingEntity.canEquipWithDispenser and the overrides of canUseSlot and canDispenserEquipIntoSlot that
    // Obsidian's mobs need: a horse takes a saddle or body armour once tamed, any other mob only while it picks up
    // loot.
    private static bool CanEquip(Mob mob, Wearable wearable)
    {
        var slot = wearable.Slot;
        if (!mob.Alive || !mob.GetEquipment(slot).IsAir || mob.Type is not EntityType type || !wearable.Fits(type))
            return false;

        if (mob is AbstractHorse horse)
        {
            var tamed = horse.HorseMask.HasFlag(HorseMask.Tamed);

            // AbstractHorse.canUseSlot: donkeys and mules take a saddle grown and tamed; horses and llamas any slot.
            if (slot == EquipmentSlot.Saddle && horse is not (Horse or Llama) && (horse.IsBaby || !tamed))
                return false;

            if (slot is EquipmentSlot.Body or EquipmentSlot.Saddle && tamed)
                return true;
        }

        return mob.CanPickUpLoot;
    }

    private static void PlayEquipSound(Mob mob, string sound) =>
        mob.Level.PacketBroadcaster.BroadcastToLevelInRange(mob.Level, (Vector)mob.Position.Floor(), new SoundPacket
        {
            SoundLocation = sound,
            SoundPosition = new SoundPosition(mob.Position.X, mob.Position.Y, mob.Position.Z),
            Category = mob.SoundSource,
            Volume = 1,
            Pitch = 1,
            Seed = Globals.Random.NextInt64()
        });

    private sealed class EquipmentBehavior : IDispenseBehavior
    {
        public ValueTask<Dispensed> DispenseAsync(DispenseSource source, ItemStack stack) =>
            TryEquip(source, stack)
                ? ValueTask.FromResult(Dispensed.Spent(stack))
                : Drop.DispenseAsync(source, stack);
    }

    /// <summary>
    /// Vanilla's <c>ShearsDispenseItemBehavior</c>: harvests a full beehive in front, or shears the first mob there
    /// that's ready, and wears the shears for it; fails otherwise.
    /// </summary>
    private sealed class Shears : IDispenseBehavior
    {
        // Vanilla's BeehiveBlock.MAX_HONEY_LEVELS and the honeycomb a full hive gives up.
        private const int FullHoney = 5;
        private const int Honeycomb = 3;

        public async ValueTask<Dispensed> DispenseAsync(DispenseSource source, ItemStack stack)
        {
            var sheared = TryShearBeehive(source) || await TryShearMobAsync(source);
            return sheared ? Dispensed.Kept(Wear(stack)) : Dispensed.Failed(stack);
        }

        // Vanilla's tryShearBeehive: the shearing sound, three honeycomb, and the hive emptied of honey.
        private static bool TryShearBeehive(DispenseSource source)
        {
            var level = source.Level;
            var target = source.Target;
            lock (level.Fluids.TickLock)
            {
                var hive = level.BlockUpdates.GetBlock(target);
                var honey = int.TryParse(hive.GetProperty("honey_level"), out var parsed) ? parsed : -1;
                if (hive.BlockClass() != "BeehiveBlock" || honey < FullHoney)
                    return false;

                var center = Center(target);
                level.PacketBroadcaster.BroadcastToLevelInRange(level, target, new SoundPacket
                {
                    SoundLocation = "minecraft:block.beehive.shear",
                    SoundPosition = new SoundPosition(center.X, center.Y, center.Z),
                    Category = SoundCategory.Blocks,
                    Volume = 1,
                    Pitch = 1,
                    Seed = Globals.Random.NextInt64()
                });

                level.DropItemStack(target, new ItemStack(ItemsRegistry.Honeycomb, Honeycomb));
                level.BlockUpdates.SetBlock(target, hive.WithProperty("honey_level", 0));
                level.EmitGameEvent(MobGameEvent.Shear, center);
                return true;
            }
        }

        // Vanilla's tryShearEntity: the first mob in front that's ready.
        private static async ValueTask<bool> TryShearMobAsync(DispenseSource source)
        {
            var level = source.Level;
            var min = (VectorD)source.Target;
            var shearable = level.EntitiesIn(min, min + new VectorD(1, 1, 1))
                .OfType<IShearable>()
                .FirstOrDefault(mob => mob.ReadyForShearing);
            if (shearable is null)
                return false;

            await shearable.ShearAsync(SoundCategory.Blocks);
            level.EmitGameEvent(MobGameEvent.Shear, Center(source.Target));
            return true;
        }
    }

    /// <summary>
    /// Vanilla's <c>SpawnEggItemBehavior</c>: the egg's mob stands in front, settled onto what's under it unless the
    /// dispenser faces up, facing a random way.
    /// </summary>
    private sealed class SpawnEggBehavior : IDispenseBehavior
    {
        public ValueTask<Dispensed> DispenseAsync(DispenseSource source, ItemStack stack)
        {
            var level = source.Level;
            if (SpawnEggs.TypeOf(stack) is not EntityType type || EntitySpawner.CreateMob(level, type) is not Mob mob)
                return ValueTask.FromResult(Dispensed.Kept(stack));

            var target = source.Target;
            var height = source.Facing != BlockFace.Up ? SpawnEggs.SettledOffset(level, mob, target) : 0;
            mob.EntityId = Server.GetNextEntityId();
            mob.Position = (VectorD)target + new VectorD(0.5, height, 0.5);

            // Vanilla's EntityType.create: a yaw drawn from the level's random.
            mob.Yaw = level.GrowthRandom.NextFloat() * 360;
            if (!level.TrySpawnEntity(mob))
                return ValueTask.FromResult(Dispensed.Kept(stack));

            level.EmitGameEvent(MobGameEvent.EntityPlace, Center(source.Position));
            return ValueTask.FromResult(Dispensed.Spent(stack));
        }
    }

    private sealed class VehicleBehavior : IDispenseBehavior
    {
        public ValueTask<Dispensed> DispenseAsync(DispenseSource source, ItemStack stack)
        {
            if (!VehicleItems.Dispense(source.Level, source.Position, source.Facing, stack))
                return Drop.DispenseAsync(source, stack);

            return ValueTask.FromResult(Dispensed.Spent(stack));
        }
    }
}
