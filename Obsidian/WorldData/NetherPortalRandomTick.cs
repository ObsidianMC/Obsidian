using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Entities;
using Obsidian.Entities.Factories;

namespace Obsidian.WorldData;

/// <summary>
/// Vanilla's <c>NetherPortalBlock.randomTick</c>: where monsters spawn, the difficulty isn't peaceful and the
/// <c>nether_portal_spawns_piglin</c> attribute holds (the overworld's types), a portal block spawns a zombified piglin
/// one time in 2000 for each level of difficulty, while a player is close enough to spawn mobs, on the floor below the
/// portal if a mob may stand there. The piglin waits out a portal cooldown before it may use the portal.
/// </summary>
internal sealed class NetherPortalRandomTick : IRandomTickBehavior
{
    // Vanilla's random.nextInt(2000) < difficulty: one draw, after every other condition but the player and floor.
    private const int SpawnChance = 2000;

    public void RandomTick(AbstractLevel level, Vector position, IBlock block, IRandomSource random)
    {
        var data = level.LevelData;

        // Vanilla's ServerLevel.isSpawningMonsters: both rules.
        if (!data.GetBooleanRule("spawn_mobs") || !data.GetBooleanRule("spawn_monsters"))
            return;

        if (data.Difficulty == Difficulty.Peaceful)
            return;

        if (!level.Gameplay(LevelEnvironment.NetherPortalSpawnsPiglin, position))
            return;

        if (random.NextInt(SpawnChance) >= (int)data.Difficulty)
            return;

        if (!MobSpawner.AnyPlayerCloseEnoughForSpawning(level, position))
            return;

        var floor = position;
        while (level.BlockUpdates.GetBlock(floor).Is(BlockIds.NetherPortal))
            floor += Vector.Down;

        if (!MobSpawner.IsSpawnableFloor(level.BlockUpdates.GetBlock(floor)))
            return;

        Spawn(level, floor + Vector.Up, random);
    }

    // Vanilla's EntityType.spawn for a STRUCTURE spawn: the piglin stands at the centre of the block above the floor,
    // turned by a draw of the level's random, finalized (which can add a chicken jockey it rides first), added and
    // heard, then given its portal cooldown, and its vehicle too.
    private static void Spawn(AbstractLevel level, Vector position, IRandomSource random)
    {
        var piglin = (ZombifiedPiglin)EntitySpawner.CreateMob(level, EntityType.ZombifiedPiglin)!;
        Angle yaw = WrapDegrees(random.NextFloat() * 360f);
        piglin.EntityId = Server.GetNextEntityId();
        piglin.Position = new VectorD(position.X + 0.5, position.Y, position.Z + 0.5);
        piglin.Yaw = yaw;
        piglin.Pitch = 0;
        piglin.InitializeAi(finalizeSpawn: false);
        piglin.LookControl.SnapHeadYaw(yaw);
        var jockey = piglin.FinalizeSpawnFromLevel(level, random);
        if (jockey is not null && !level.TrySpawnEntity(jockey))
            jockey = null;

        if (!level.TrySpawnEntity(piglin))
            return;

        if (jockey is not null)
            piglin.StartRiding(jockey);

        piglin.PlaySpawnAmbientSound(level);
        piglin.StartPortalCooldown();
        if (piglin.Vehicle is Entity vehicle)
            vehicle.StartPortalCooldown();
    }

    // Vanilla's Mth.wrapDegrees: the angle in [-180, 180).
    private static float WrapDegrees(float degrees)
    {
        var wrapped = degrees % 360f;
        if (wrapped >= 180f)
            wrapped -= 360f;

        if (wrapped < -180f)
            wrapped += 360f;

        return wrapped;
    }
}

internal static partial class RandomTickBehaviors
{
    internal static IRandomTickBehavior NetherPortalBlock { get; } = new NetherPortalRandomTick();
}
