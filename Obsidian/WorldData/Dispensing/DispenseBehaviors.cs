using Obsidian.API.Inventory;
using Obsidian.Entities;
using Obsidian.WorldData.Features;
using System.Collections.Frozen;

namespace Obsidian.WorldData.Dispensing;

/// <summary>
/// What a dispenser does with an item it fires, like vanilla's <c>DispenseItemBehavior</c>s: shoot it, use it on the
/// block or the mobs in front, or drop it.
/// </summary>
internal interface IDispenseBehavior
{
    /// <summary>
    /// Fires one of <paramref name="stack"/>, the stack in the dispenser's chosen slot.
    /// </summary>
    /// <remarks>
    /// Commits synchronously under tick then try-inventory, with external block effects deferred until unlock.
    /// Must not load chunks, roll container loot, await, or invoke plugin code.
    /// </remarks>
    /// <param name="source">The dispenser.</param>
    /// <param name="stack">A copy of the slot's stack, which the behaviour may keep.</param>
    /// <returns>What the slot holds afterwards, and the level event the dispenser plays.</returns>
    Dispensed Dispense(DispenseSource source, ItemStack stack);
}

/// <summary>
/// What a dispense left in the slot (null when it's empty), and the level event the dispenser plays for it: vanilla's
/// <c>SOUND_DISPENSER_DISPENSE</c> (1000), <c>SOUND_DISPENSER_FAIL</c> (1001) or a launch sound.
/// </summary>
internal readonly record struct Dispensed(ItemStack? Rest, int Event)
{
    // Vanilla's LevelEvent.SOUND_DISPENSER_DISPENSE and SOUND_DISPENSER_FAIL.
    public const int DispenseEvent = 1000;
    public const int FailEvent = 1001;

    /// <summary>The item was used: one fewer is left, with the dispense sound.</summary>
    public static Dispensed Spent(ItemStack stack) => new(DispenseSource.Shrink(stack), DispenseEvent);

    /// <summary>Nothing happened: the stack stays, with the fail sound.</summary>
    public static Dispensed Failed(ItemStack stack) => new(stack, FailEvent);

    /// <summary>The stack stays as <paramref name="stack"/> is now, with the dispense sound.</summary>
    public static Dispensed Kept(ItemStack? stack) => new(stack, DispenseEvent);
}

/// <summary>
/// The dispenser firing: its level, position, state and facing, and its inventory, which the dispenser holds locked
/// while it fires (vanilla's <c>BlockSource</c>).
/// </summary>
internal sealed record DispenseSource(AbstractLevel Level, Vector Position, IBlock State, BlockFace Facing,
    BaseContainer Inventory)
{
    internal BlockPlacement.Prepared? Placement { get; init; }

    internal BucketPreparation? Bucket { get; init; }

    /// <summary>The block in front of the dispenser.</summary>
    public Vector Target => this.Position + this.Facing.ToVector();

    /// <summary>
    /// Vanilla's <c>DispenserBlock.getDispensePosition</c>: <paramref name="distance"/> blocks out of the dispenser's
    /// centre along its facing.
    /// </summary>
    public VectorD Out(double distance)
    {
        var step = (VectorD)this.Facing.ToVector();
        return (VectorD)this.Position + new VectorD(0.5, 0.5, 0.5) + step * distance;
    }

    /// <summary>The stack with one fewer, or null when that was the last.</summary>
    public static ItemStack? Shrink(ItemStack stack) => stack.Count > 1 ? new ItemStack(stack, stack.Count - 1) : null;
}

/// <summary>
/// The dispense behaviours of vanilla's items, by item, as vanilla's <c>DispenseItemBehavior.bootStrap</c> registers
/// them, and the defaults it picks for the others (<c>DispenserBlock.getDefaultDispenseMethod</c>).
/// </summary>
internal static partial class DispenseBehaviors
{
    /// <summary>Vanilla's <c>DefaultDispenseItemBehavior</c>: drops one of the stack.</summary>
    internal static IDispenseBehavior Drop { get; } = new DropBehavior();

    private static readonly FrozenDictionary<int, IDispenseBehavior> byItem = Register();

    /// <summary>The behaviour a dispenser fires <paramref name="stack"/> with.</summary>
    internal static IDispenseBehavior For(ItemStack stack)
    {
        if (byItem.TryGetValue(stack.Holder.Id, out var behavior))
            return behavior;

        if (VehicleItems.IsVehicle(stack))
            return Vehicle;

        // Vanilla's getDefaultDispenseMethod: equipment, then spawn eggs, then the drop.
        if (Wearable.Of(stack) is not null)
            return Equipment;

        if (SpawnEggs.TypeOf(stack) is not null)
            return SpawnEgg;

        return Drop;
    }

    // Construct and populate an unpublished box before either commit lock; plugin factories/components may run here.
    internal static BlockPlacement.Prepared? Prepare(DispenseSource source, ItemStack stack)
    {
        if (!stack.Holder.UnlocalizedName.EndsWith("shulker_box", StringComparison.Ordinal)
            || !BlocksRegistry.TryGetBlock(stack.Holder, out var box))
        {
            return null;
        }

        return BlockPlacement.ForDispenser(new ItemStack(stack, 1))
            .Prepare(box, source.Target, source.Level.Logger);
    }

    private static FrozenDictionary<int, IDispenseBehavior> Register()
    {
        var behaviors = new Dictionary<int, IDispenseBehavior>();
        RegisterProjectiles(behaviors);
        RegisterBlocks(behaviors);
        RegisterEntities(behaviors);
        RegisterFluids(behaviors);
        return behaviors.ToFrozenDictionary();
    }

    /// <summary>
    /// Vanilla's <c>DefaultDispenseItemBehavior.spawnItem</c>: one item from 0.7 blocks out of the centre, lowered by
    /// a little, with the item entity's own motion drawn and replaced by one along the facing.
    /// </summary>
    /// <remarks>
    /// Draws from the level's random in vanilla's order: the two draws of the item entity's own motion, then the
    /// speed and three triangles, as vanilla draws them from its level's random.
    /// </remarks>
    internal static void DropItem(AbstractLevel level, Vector position, BlockFace facing, ItemStack item)
    {
        // Vanilla DefaultDispenseItemBehavior: an item's speed (6) times its spread per unit of speed.
        const double spread = 6 * 0.0172275;

        var step = facing.ToVector();
        var lowered = facing.IsVertical() ? 0.125 : 0.15625;
        var x = position.X + 0.5 + 0.7 * step.X;
        var y = position.Y + 0.5 + 0.7 * step.Y - lowered;
        var z = position.Z + 0.5 + 0.7 * step.Z;

        var random = level.GrowthRandom;
        random.NextDouble();
        random.NextDouble();

        var speed = random.NextDouble() * 0.1 + 0.2;
        var motion = new VectorD(
            random.Triangle(step.X * speed, spread),
            random.Triangle(0.2, spread),
            random.Triangle(step.Z * speed, spread));

        level.TrySpawnEntity(new ItemEntity
        {
            Level = level,
            EntityId = Server.GetNextEntityId(),
            Position = new VectorD(x, y, z),
            Item = item,
            Motion = motion
        }, hostOwned: true);
    }

    /// <summary>Vanilla's <c>DefaultDispenseItemBehavior</c>: drops one of the stack.</summary>
    private sealed class DropBehavior : IDispenseBehavior
    {
        public Dispensed Dispense(DispenseSource source, ItemStack stack)
        {
            DropItem(source.Level, source.Position, source.Facing, new ItemStack(stack, 1));
            return Dispensed.Spent(stack);
        }
    }
}
