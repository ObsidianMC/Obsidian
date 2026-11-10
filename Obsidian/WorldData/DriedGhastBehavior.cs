using Obsidian.API.Blocks;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Entities;
using Obsidian.Entities.Factories;

namespace Obsidian.WorldData;

/// <summary>
/// Vanilla's <c>DriedGhastBlock</c> <c>randomTick</c> and <c>tick</c>: a dried ghast in water, or one still wet, asks
/// for a tick 5000 ticks later on a random tick. In water each tick hydrates it a stage, and at the last stage it
/// becomes a ghastling (a baby happy ghast) facing the way the block faced, leaving its water behind; out of water each
/// tick dries it a stage.
/// </summary>
internal sealed class DriedGhastBehavior : IRandomTickBehavior, IScheduledTickBehavior
{
    // Vanilla's HYDRATION_TICK_DELAY and MAX_HYDRATION_LEVEL.
    private const int HydrationDelay = 5000;

    private const int MaxHydration = 3;

    public void RandomTick(AbstractLevel level, Vector position, IBlock block, IRandomSource random)
    {
        var blocks = level.BlockUpdates;
        if (!IsWaterlogged(block) && Hydration(block) == 0)
            return;

        if (!blocks.HasScheduledTick(position, block))
            blocks.ScheduleTick(position, block, HydrationDelay);
    }

    public void Tick(IBlockLevel level, Vector position, IBlock block, IRandomSource random)
    {
        var concrete = (AbstractLevel)level.Level;
        var hydration = Hydration(block);
        if (!IsWaterlogged(block))
        {
            if (hydration > 0)
                SetHydration(concrete, level, position, block, hydration - 1);

            return;
        }

        if (hydration == MaxHydration)
        {
            SpawnGhastling(concrete, level, position, block);
            return;
        }

        LevelSounds.PlayAtBlock(concrete, position, "minecraft:block.dried_ghast.transition");
        SetHydration(concrete, level, position, block, hydration + 1);
    }

    // Vanilla stores the stage for clients only (UPDATE_CLIENTS) and emits a block change carrying the old state.
    private static void SetHydration(AbstractLevel level, IBlockLevel blocks, Vector position, IBlock block, int stage)
    {
        blocks.SetBlock(position, block.WithProperty("hydration", stage.ToString()), BlockUpdateFlags.Clients);
        level.EmitGameEvent(MobGameEvent.BlockChange, (VectorD)position + 0.5, affectedBlock: block);
    }

    // Vanilla's spawnGhastling: the block goes (its water stays), and a baby happy ghast stands at the bottom centre of
    // its place, turned the way the block faced, with its spawn sound following it. Vanilla doesn't finalize its spawn,
    // and never finds the level refusing it; where Obsidian's does, the dried ghast goes back rather than being lost.
    private static void SpawnGhastling(AbstractLevel level, IBlockLevel blocks, Vector position, IBlock block)
    {
        blocks.RemoveBlock(position);

        var ghastling = (HappyGhast)EntitySpawner.CreateMob(level, EntityType.HappyGhast)!;
        Angle yaw = FacingYaw(block.GetProperty("facing"));
        ghastling.EntityId = Server.GetNextEntityId();
        ghastling.Position = new VectorD(position.X + 0.5, position.Y, position.Z + 0.5);
        ghastling.Yaw = yaw;
        ghastling.Pitch = 0;
        ghastling.InitializeAi(finalizeSpawn: false);
        ghastling.IsBaby = true;
        ghastling.LookControl.SnapHeadYaw(yaw);

        if (!level.TrySpawnEntity(ghastling))
        {
            blocks.SetBlock(position, block);
            return;
        }

        LevelSounds.PlayFrom(level, ghastling, "minecraft:entity.ghastling.spawn", SoundCategory.Blocks);
    }

    // Vanilla's Direction.getYRot for the block's facing.
    private static float FacingYaw(string? facing) => facing switch
    {
        "south" => 0,
        "west" => 90,
        "north" => 180,
        _ => -90
    };

    private static bool IsWaterlogged(IBlock block) => block.GetProperty("waterlogged") == "true";

    private static int Hydration(IBlock block) => int.Parse(block.GetProperty("hydration")!);
}

internal static partial class RandomTickBehaviors
{
    internal static IRandomTickBehavior DriedGhastBlock { get; } = new DriedGhastBehavior();
}
