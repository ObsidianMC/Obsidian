using Obsidian.API.Blocks;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Entities;
using Obsidian.WorldData.BlockNotifications;

namespace Obsidian.WorldData;

/// <summary>Dripleaf's inside contact, persisted tilt ticks and redstone reset share the same state transition.</summary>
internal sealed class BigDripleafContactBehavior : IEntityInsideBehavior, IScheduledTickBehavior, INeighborChangedBehavior
{
    internal static BigDripleafContactBehavior Instance { get; } = new();

    public ValueTask EntityInsideAsync(IContactLevel level, Vector position, IBlock block, Entity entity)
    {
        if (block.GetProperty("tilt") == "none" && (entity.MovementFlags & MovementFlags.OnGround) != 0
            && entity.Position.Y > position.Y + 0.6875 && !level.Blocks.HasNeighborSignal(position))
        {
            Change(level.Blocks, position, block, "unstable", delay: 10);
        }

        return default;
    }

    public void Tick(IBlockLevel level, Vector position, IBlock block, IRandomSource random)
    {
        if (level.HasNeighborSignal(position))
        {
            Reset(level, position, block);
            return;
        }

        switch (block.GetProperty("tilt"))
        {
            case "unstable":
                Change(level, position, block, "partial", "down", 10);
                break;
            case "partial":
                Change(level, position, block, "full", "down", 100);
                break;
            case "full":
                Reset(level, position, block);
                break;
        }
    }

    public ValueTask OnNeighborChangedAsync(NeighborChange change, IBlockChangeWorld world)
    {
        if (world is AbstractLevel level && level.HasNeighborSignal(change.Position))
            Reset(level.BlockUpdates, change.Position, change.State);

        return default;
    }

    internal static ValueTask ProjectileHit(IBlockLevel level, Vector position, IBlock block)
    {
        Change(level, position, block, "full", "down", 100);
        return default;
    }

    private static void Reset(IBlockLevel level, Vector position, IBlock block)
    {
        if (block.GetProperty("tilt") != "none")
            Change(level, position, block, "none", "up");
    }

    private static void Change(IBlockLevel level, Vector position, IBlock block, string tilt,
        string? sound = null, int delay = 0)
    {
        if (level.Level is not AbstractLevel world)
            return;

        lock (world.Fluids.TickLock)
        {
            if (!level.GetBlock(position).IsSameState(block))
                return;

            level.SetBlock(position, block.WithProperty("tilt", tilt), BlockUpdateFlags.Clients);
            if (tilt != "unstable" && tilt != block.GetProperty("tilt"))
                world.EmitGameEvent(MobGameEvent.BlockChange, (VectorD)position + 0.5);

            if (sound is not null)
            {
                // Vanilla draws one level float after the write/vibration, for a pitch between 0.8 and 1.2.
                var pitch = 0.8f + level.Random.NextFloat() * 0.4f;
                LevelSounds.PlayAtBlock(world, position, $"minecraft:block.big_dripleaf.tilt_{sound}", pitch: pitch);
            }
            if (delay > 0)
                level.ScheduleTick(position, block, delay);
        }
    }
}

/// <summary>A shrieker is stepped on directly, including while sneaking; no movement vibration is required.</summary>
internal sealed class ShriekerContactBehavior : IStepOnBehavior, IScheduledTickBehavior
{
    public ValueTask SteppedOnAsync(IContactLevel level, Vector position, IBlock block, Entity entity)
    {
        var player = entity as IPlayer;
        if (player is null && entity.Passengers.Count > 0)
            player = entity.Passengers[0] as IPlayer;

        if (player is not null && level.Level is AbstractLevel world)
            return world.ShriekAsync(position, player);

        return default;
    }

    public void Tick(IBlockLevel level, Vector position, IBlock block, IRandomSource random)
    {
        if (block.GetProperty("shrieking") == "true" && level.Level is AbstractLevel world)
            world.FinishShriek(position, block);
    }
}
