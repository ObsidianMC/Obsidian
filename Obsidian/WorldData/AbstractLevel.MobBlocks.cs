using Obsidian.Entities;
using Obsidian.Entities.AI;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    // Vanilla's LevelEvent.PARTICLES_DESTROY_BLOCK, which each block a built mob takes the place of plays.
    private const int DestroyBlockEvent = 2001;

    // The axes a wither's T stands along, the way vanilla's pattern search finds it first: its columns are cleared
    // from the positive end of the axis to the negative.
    private static readonly Vector[] witherAxes = [new(1, 0, 0), new(0, 0, 1)];

    private void CheckWither(Vector position, IBlock block)
    {
        if (block.RegistryId is not BlockIds.WitherSkeletonSkull and not BlockIds.WitherSkeletonWallSkull)
            return;

        EnqueueEntityAction(async () => await BuildWitherAsync(position));
    }

    private void CheckSnowGolem(Vector position, IBlock block)
    {
        if (block.RegistryId is not BlockIds.CarvedPumpkin and not BlockIds.JackOLantern)
            return;

        EnqueueEntityAction(async () => await BuildSnowGolemAsync(position));
    }

    /// <summary>
    /// Vanilla's <c>CarvedPumpkinBlock.canSpawnGolem</c> for snow golems: whether a pumpkin at <paramref name="head"/>
    /// would stand on the two snow blocks of one. Iron and copper golems aren't built yet.
    /// </summary>
    internal bool CanBuildSnowGolem(Vector head)
    {
        var terrain = new MobTerrain(this);
        return terrain.GetBlock(head - new Vector(0, 1, 0))?.RegistryId == BlockIds.SnowBlock
            && terrain.GetBlock(head - new Vector(0, 2, 0))?.RegistryId == BlockIds.SnowBlock;
    }

    /// <summary>
    /// Vanilla's <c>CarvedPumpkinBlock.trySpawnGolem</c> for snow golems: a pumpkin or jack o'lantern at
    /// <paramref name="head"/> on two snow blocks turns into a snow golem, each block playing its breaking effect,
    /// from the top down.
    /// </summary>
    /// <returns>Whether a snow golem was built.</returns>
    internal ValueTask<bool> BuildSnowGolemAsync(Vector head)
    {
        // The pattern is checked and cleared in one tick-lock section, so a block placed meanwhile isn't removed.
        lock (this.Fluids.TickLock)
            return ValueTask.FromResult(this.BuildDispensedSnowGolem(head));
    }

    // Synchronous resident-only commit; call holding the tick lock (a dispenser's commit holds it already).
    internal bool BuildDispensedSnowGolem(Vector head)
    {
        var terrain = new MobTerrain(this);
        Vector[] column = [head, head - new Vector(0, 1, 0), head - new Vector(0, 2, 0)];
        if (terrain.GetBlock(column[0])?.RegistryId is not BlockIds.CarvedPumpkin and not BlockIds.JackOLantern
            || !this.CanBuildSnowGolem(head))
        {
            return false;
        }

        this.ClearDispensedPattern(terrain, column);

        var bottom = column[^1];
        SpawnEntity(new SnowGolem
        {
            Level = this,
            EntityId = Server.GetNextEntityId(),
            Position = new VectorD(bottom.X + 0.5f, bottom.Y + 0.05f, bottom.Z + 0.5f)
        });
        return true;
    }

    /// <summary>
    /// Vanilla's <c>WitherSkullBlock.canSpawnMob</c>: whether a wither skeleton skull at <paramref name="skull"/>
    /// would top a wither's T of soul sand or soul soil, along either axis, with air beside its foot. The other two
    /// skulls needn't be there yet.
    /// </summary>
    internal bool CanBuildWither(Vector skull) =>
        skull.Y >= this.MinY + 2
        && LevelData.Difficulty != Difficulty.Peaceful
        && this.FindWitherBase(new MobTerrain(this), skull, skulls: false) is not null;

    /// <summary>
    /// Vanilla's <c>WitherSkullBlock.checkSpawn</c>: three wither skeleton skulls on a T of soul sand or soul soil,
    /// one of them at <paramref name="skull"/>, turn into a wither, each block of the pattern (the air beside its foot
    /// too) playing its breaking effect, column by column.
    /// </summary>
    /// <returns>Whether a wither was built.</returns>
    internal ValueTask<bool> BuildWitherAsync(Vector skull)
    {
        // The pattern is checked and cleared in one tick-lock section, so a block placed meanwhile isn't removed.
        lock (this.Fluids.TickLock)
            return ValueTask.FromResult(this.BuildDispensedWither(skull));
    }

    // Synchronous resident-only commit; call holding the tick lock (a dispenser's commit holds it already).
    internal bool BuildDispensedWither(Vector skull)
    {
        if (skull.Y < this.MinY || LevelData.Difficulty == Difficulty.Peaceful)
            return false;

        var terrain = new MobTerrain(this);
        if (this.FindWitherBase(terrain, skull, skulls: true) is not (Vector center, Vector axis))
            return false;

        var cells = new List<Vector>();
        foreach (var side in new[] { 1, 0, -1 })
        {
            var top = center + axis * side;
            cells.Add(top);
            cells.Add(top - new Vector(0, 1, 0));
            cells.Add(top - new Vector(0, 2, 0));
        }

        this.ClearDispensedPattern(terrain, cells);

        var foot = center - new Vector(0, 2, 0);
        var wither = new Wither
        {
            Level = this,
            EntityId = Server.GetNextEntityId(),
            Position = new VectorD(foot.X + 0.5, foot.Y + 0.55, foot.Z + 0.5),
            Yaw = axis.X == 0 ? 90 : 0
        };
        SpawnEntity(wither);
        wither.BeginSpawnCharge();
        return true;
    }

    // The middle of the top row and the axis of a wither's T that has the skull at skull in its top row: soul sand or
    // soul soil in the body row and under its middle, air beside its foot, and when skulls is set, skulls all along
    // the top row.
    private (Vector Center, Vector Axis)? FindWitherBase(MobTerrain terrain, Vector skull, bool skulls)
    {
        bool IsSoul(Vector point) => terrain.GetBlock(point)?.RegistryId is BlockIds.SoulSand or BlockIds.SoulSoil;

        bool IsSkull(Vector point) =>
            terrain.GetBlock(point)?.RegistryId is BlockIds.WitherSkeletonSkull or BlockIds.WitherSkeletonWallSkull;

        foreach (var axis in witherAxes)
        {
            for (var offset = -1; offset <= 1; offset++)
            {
                var center = skull + axis * offset;
                var body = center - new Vector(0, 1, 0);
                var foot = center - new Vector(0, 2, 0);
                var bodyIsSoul = IsSoul(body - axis) && IsSoul(body) && IsSoul(body + axis) && IsSoul(foot);
                var footIsClear = terrain.GetBlock(foot - axis)?.IsAir == true
                    && terrain.GetBlock(foot + axis)?.IsAir == true;
                var topIsSkulls = !skulls || IsSkull(center - axis) && IsSkull(center) && IsSkull(center + axis);
                if (bodyIsSoul && footIsClear && topIsSkulls)
                    return (center, axis);
            }
        }

        return null;
    }

    // Each consumed pattern block plays its breaking event before its resident-only removal. The dispenser's
    // state scope defers notifications; the ordinary construction path publishes normally.
    private void ClearDispensedPattern(MobTerrain terrain, IEnumerable<Vector> cells)
    {
        foreach (var cell in cells)
        {
            var state = terrain.GetBlock(cell) ?? BlocksRegistry.Air;
            this.BroadcastLevelEvent(DestroyBlockEvent, cell, state.GetHashCode());
            if (!state.IsAir)
                this.BlockUpdates.SetBlock(cell, BlocksRegistry.Air, API.Blocks.BlockUpdateFlags.All);
        }
    }
}
