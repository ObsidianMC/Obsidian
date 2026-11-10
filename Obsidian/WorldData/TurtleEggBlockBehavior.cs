using Obsidian.API.Blocks;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Entities;
using Obsidian.Registries;

namespace Obsidian.WorldData;

internal static partial class RandomTickBehaviors
{
    internal static IRandomTickBehavior TurtleEggBlock { get; } = new TurtleEggBlockBehavior();
}

/// <summary>
/// Vanilla's <c>TurtleEggBlock</c>: its random tick, which cracks an egg on sand one step at a time and then hatches
/// it, and its <c>onPlace</c>, which shows particles when it sits on sand.
/// </summary>
/// <remarks>
/// Trampling is <see cref="AbstractLevel.TrampleTurtleEggAsync"/>'s.
/// </remarks>
internal sealed class TurtleEggBlockBehavior : IRandomTickBehavior, IOnPlaceBehavior
{
    private const int LastHatchStage = 2;

    // Vanilla's level events: a block breaking (with the block's state), and a turtle egg's particles on sand.
    private const int BreakEvent = 2001;
    private const int PlacedOnSandEvent = 2012;
    private const int PlacedOnSandParticles = 15;

    private const float SoundVolume = 0.7f;

    /// <summary>
    /// Vanilla's <c>randomTick</c>. A draw of <c>nextFloat</c> against the hatch chance there (see
    /// <see cref="LevelEnvironment.TurtleEggHatchChance"/>) decides whether anything happens; then, on a block of
    /// <c>#sand</c>, a draw for the sound's pitch, and the eggs either crack a step or hatch.
    /// </summary>
    /// <remarks>
    /// Vanilla draws the chance from the level's random and the pitch from the tick's, which are the same random.
    /// </remarks>
    public void RandomTick(AbstractLevel level, Vector position, IBlock block, IRandomSource random)
    {
        var chance = level.TurtleEggHatchChance(position);
        if (chance <= 0 || random.NextFloat() >= chance)
            return;

        if (!IsOnSand(level.BlockUpdates, position))
            return;

        var pitch = 0.9f + random.NextFloat() * 0.2f;
        var hatch = Property(block, "hatch");

        if (hatch < LastHatchStage)
            Crack(level, position, block, hatch, pitch);
        else
            Hatch(level, position, block, pitch);
    }

    /// <summary>
    /// Vanilla's <c>onPlace</c>, run on every change to a turtle egg's state: on sand, particles.
    /// </summary>
    public void OnPlace(IBlockLevel level, Vector position, IBlock state, IBlock oldState)
    {
        if (level.Level is AbstractLevel concrete && IsOnSand(level, position))
            concrete.BroadcastLevelEvent(PlacedOnSandEvent, position, PlacedOnSandParticles);
    }

    // Vanilla sets the next stage for clients only (UPDATE_CLIENTS), so neighbours are told nothing beyond their
    // shapes.
    private static void Crack(AbstractLevel level, Vector position, IBlock block, int hatch, float pitch)
    {
        LevelSounds.PlayAtBlock(level, position, "minecraft:entity.turtle.egg_crack", SoundVolume, pitch);
        level.BlockUpdates.SetBlock(position, block.WithProperty("hatch", hatch + 1), BlockUpdateFlags.Clients);
        level.EmitGameEvent(MobGameEvent.BlockChange, Center(position), affectedBlock: block);
    }

    // Each egg breaks with its own particles and leaves a baby turtle that calls the spot home, the eggs spaced along
    // x as vanilla places them.
    private static void Hatch(AbstractLevel level, Vector position, IBlock block, float pitch)
    {
        LevelSounds.PlayAtBlock(level, position, "minecraft:entity.turtle.egg_hatch", SoundVolume, pitch);
        level.BlockUpdates.RemoveBlock(position);
        level.EmitGameEvent(MobGameEvent.BlockDestroy, Center(position), affectedBlock: block);

        var eggs = Property(block, "eggs");
        for (var index = 0; index < eggs; index++)
        {
            level.BroadcastLevelEvent(BreakEvent, position, block.GetHashCode());

            var spawn = new VectorD(position.X + 0.3 + index * 0.2, position.Y, position.Z + 0.3);
            var turtle = (Turtle)level.GetNewEntitySpawner()
                .WithEntityType(EntityType.Turtle)
                .AtPosition(spawn)
                .AsBaby()
                .Spawn();
            turtle.SetHome(position);
        }
    }

    // Vanilla's TurtleEggBlock.onSand: the block below is in #sand.
    private static bool IsOnSand(IBlockLevel level, Vector position) =>
        TagsRegistry.Block.Sand.Entries.Contains(level.GetBlock(position + Vector.Down).RegistryId);

    private static int Property(IBlock block, string name) =>
        int.TryParse(block.GetProperty(name), out var value) ? value : 0;

    private static VectorD Center(Vector position) => (VectorD)position + new VectorD(0.5, 0.5, 0.5);
}
