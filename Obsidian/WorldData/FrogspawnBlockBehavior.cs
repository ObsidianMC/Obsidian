using Obsidian.API.Blocks;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Entities;

namespace Obsidian.WorldData;

/// <summary>
/// Vanilla's <c>FrogspawnBlock</c>: however it got there (laid by a frog, placed by a player or set by a command), it
/// schedules its hatch when it's placed, and on that tick it hatches into tadpoles if it still sits where it can.
/// </summary>
internal sealed class FrogspawnBlockBehavior : IOnPlaceBehavior, IScheduledTickBehavior
{
    // Vanilla's FrogspawnBlock: the hatch tick comes 3600 to 11999 ticks after it's placed, and 2 to 5 tadpoles hatch.
    private const int MinHatchDelay = 3600;
    private const int MaxHatchDelay = 12000;
    private const int MinTadpoles = 2;
    private const int MaxTadpoles = 5;

    // Vanilla clamps each tadpole's offset across the block to these: the low one is a float widened to a double, the
    // high one a double just under 0.8. They decide where tadpoles appear, so they're kept exactly.
    private const double MinTadpoleOffset = 0.2f;
    private const double MaxTadpoleOffset = 0.7999999970197678;

    // Vanilla's level event of a block breaking, with the block's state.
    private const int BreakEvent = 2001;

    /// <summary>
    /// Vanilla's <c>onPlace</c>, run on every change to a frogspawn's state: schedules its hatch with a draw from the
    /// level's random. Like vanilla, it draws even when a hatch is already scheduled, which then stays as it was.
    /// </summary>
    public void OnPlace(IBlockLevel level, Vector position, IBlock state, IBlock oldState)
    {
        var delay = level.Random.NextInt(MinHatchDelay, MaxHatchDelay);
        level.ScheduleTick(position, state, delay);
    }

    /// <summary>
    /// Vanilla's <c>tick</c>: frogspawn that can't survive where it is breaks; otherwise it breaks, plays its hatching
    /// sound and leaves tadpoles in the water below.
    /// </summary>
    public void Tick(IBlockLevel level, Vector position, IBlock block, IRandomSource random)
    {
        if (level.Level is not AbstractLevel concrete)
            return;

        var survives = PlacementSurvival.CanSurvive(level, block, position);
        Destroy(concrete, level, position, block);
        if (!survives)
            return;

        BlockSounds.Play(concrete, position, "minecraft:block.frogspawn.hatch", 1f, 1f);
        SpawnTadpoles(concrete, position, random);
    }

    // Vanilla's Level.destroyBlock without drops: the block's breaking effects, its fluid left behind and a game event.
    private static void Destroy(AbstractLevel concrete, IBlockLevel level, Vector position, IBlock block)
    {
        concrete.BroadcastLevelEvent(BreakEvent, position, block.GetHashCode());
        level.RemoveBlock(position);

        var center = (VectorD)position + new VectorD(0.5, 0.5, 0.5);
        concrete.EmitGameEvent(MobGameEvent.BlockDestroy, center, affectedBlock: block);
    }

    // Each tadpole takes an x and a z offset and a yaw of 1 to 360 degrees, in that order, and starts half a block
    // down, in the water. Tadpoles from frogspawn never despawn.
    private static void SpawnTadpoles(AbstractLevel level, Vector position, IRandomSource random)
    {
        var count = random.NextInt(MinTadpoles, MaxTadpoles + 1);
        for (var index = 0; index < count; index++)
        {
            var x = position.X + TadpoleOffset(random);
            var z = position.Z + TadpoleOffset(random);
            var yaw = random.NextInt(1, 361);

            level.SpawnEntity(new Tadpole
            {
                Level = level,
                EntityId = Server.GetNextEntityId(),
                Position = new VectorD(x, position.Y - 0.5, z),
                Yaw = yaw,
                PersistenceRequired = true
            });
        }
    }

    private static double TadpoleOffset(IRandomSource random) =>
        Math.Clamp(random.NextDouble(), MinTadpoleOffset, MaxTadpoleOffset);
}
