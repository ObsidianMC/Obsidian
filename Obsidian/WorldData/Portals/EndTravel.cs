using Obsidian.API.World;
using Obsidian.Entities;

namespace Obsidian.WorldData.Portals;

/// <summary>
/// Where End portals and End gateways send entities other than players, through the same teleport as other portals.
/// Players keep their own End paths: credits, their respawn point and their gateway cooldown.
/// </summary>
internal static class EndTravel
{
    /// <summary>Vanilla's <c>ServerLevel.END_SPAWN_POINT</c>, where End portals bring entities into the End.</summary>
    internal static readonly Vector EndSpawnPoint = new(100, 50, 0);

    /// <summary>
    /// Clears the obsidian arrival platform below <see cref="EndSpawnPoint"/>, like vanilla's
    /// <c>EndPlatformFeature</c>: a 5 by 5 obsidian floor at y 48 with three air layers above it.
    /// </summary>
    internal static async ValueTask BuildArrivalPlatformAsync(AbstractLevel end)
    {
        for (var x = EndSpawnPoint.X - 2; x <= EndSpawnPoint.X + 2; x++)
            for (var z = EndSpawnPoint.Z - 2; z <= EndSpawnPoint.Z + 2; z++)
                for (var y = EndSpawnPoint.Y - 2; y <= EndSpawnPoint.Y + 1; y++)
                {
                    var block = y == EndSpawnPoint.Y - 2 ? BlocksRegistry.ObsidianBlock : BlocksRegistry.Air;
                    await end.SetBlockAsync(x, y, z, block, true);
                }
    }

    /// <summary>
    /// Prepares an End portal or End gateway trip for a non-player entity, loading the destination first. Returns
    /// null when there is nowhere to go or <paramref name="canContinue"/> turns false.
    /// </summary>
    internal static async Task<PortalDestination?> PrepareAsync(
        Entity entity, PortalContact portal, Func<bool> canContinue)
    {
        if (entity.Level is not AbstractLevel source)
            return null;

        try
        {
            return portal.BlockId == BlockIds.EndGateway
                ? await PrepareGatewayAsync(entity, source, portal.Position, canContinue)
                : await PreparePortalAsync(entity, source, canContinue);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private static async Task<PortalDestination?> PreparePortalAsync(
        Entity entity, AbstractLevel source, Func<bool> canContinue)
    {
        var world = source is IDimension dimension ? dimension.ParentWorld as World : source as World;
        if (world is null)
            return null;

        var enteringEnd = source.DimensionName != "minecraft:the_end";
        var target = enteringEnd ? world.GetLevel("minecraft:the_end") : world;
        if (target is not AbstractLevel destination)
            return null;

        if (enteringEnd)
        {
            // Vanilla EndPortalBlock: the spawn point's bottom centre facing west, keeping the pitch, with the
            // velocity turned as much as the entity turns.
            var arrival = new VectorD(EndSpawnPoint.X + 0.5, EndSpawnPoint.Y, EndSpawnPoint.Z + 0.5);
            var area = await PortalArea.LoadAsync(destination, EndSpawnPoint, 3, canContinue);
            if (area is null)
                return null;

            return await FinishAsync(area, async () =>
            {
                await BuildArrivalPlatformAsync(destination);
                const float west = 90f;
                var motion = TurnMotion(entity.Motion, entity.Yaw.Degrees - west);
                return new(destination, arrival, west, entity.Pitch, motion, area);
            }, canContinue);
        }

        // Leaving the End: the top of the world spawn's column (vanilla's adjustSpawnLocation, with the
        // MOTION_BLOCKING_NO_LEAVES heightmap), keeping rotation and velocity. Obsidian has no spawn angle to add.
        var spawn = world.LevelData.SpawnPosition;
        var column = new Vector((int)Math.Floor(spawn.X), (int)Math.Floor(spawn.Y), (int)Math.Floor(spawn.Z));
        var spawnArea = await PortalArea.LoadAsync(destination, column, 0, canContinue);
        if (spawnArea is null)
            return null;

        return await FinishAsync(spawnArea, () =>
        {
            var chunk = spawnArea.Chunks[(column.X >> 4, column.Z >> 4)];
            var y = chunk.Heightmaps[HeightmapType.MotionBlockingNoLeaves].GetHeight(column.X & 15, column.Z & 15);
            var arrival = new VectorD(column.X + 0.5, y, column.Z + 0.5);
            return ValueTask.FromResult<PortalDestination>(
                new(destination, arrival, entity.Yaw, entity.Pitch, entity.Motion, spawnArea));
        }, canContinue);
    }

    // Vanilla EndGatewayBlock: the gateway's exit in the same level, keeping rotation and velocity.
    private static async Task<PortalDestination?> PrepareGatewayAsync(
        Entity entity, AbstractLevel level, Vector gateway, Func<bool> canContinue)
    {
        if (await level.FindEndGatewayExitAsync(gateway) is not Vector exit || !canContinue())
            return null;

        var area = await PortalArea.LoadAsync(level, exit, 0, canContinue);
        if (area is null)
            return null;

        var arrival = new VectorD(exit.X + 0.5, exit.Y, exit.Z + 0.5);
        return await FinishAsync(
            area,
            () => ValueTask.FromResult<PortalDestination>(
                new(level, arrival, entity.Yaw, entity.Pitch, entity.Motion, area)),
            canContinue);
    }

    // Builds the destination while the area is held, releasing it if that fails or travel no longer applies.
    private static async Task<PortalDestination?> FinishAsync(
        PortalArea area, Func<ValueTask<PortalDestination>> build, Func<bool> canContinue)
    {
        try
        {
            var destination = await build();
            if (canContinue())
                return destination;

            area.Dispose();
            return null;
        }
        catch
        {
            area.Dispose();
            throw;
        }
    }

    // Vanilla's ROTATE_DELTA teleport flag: the velocity turns about the vertical axis by the yaw the entity loses.
    private static VectorD TurnMotion(VectorD motion, float yawChange)
    {
        var (sin, cos) = Math.SinCos(yawChange * Math.PI / 180);
        return new VectorD(motion.X * cos + motion.Z * sin, motion.Y, motion.Z * cos - motion.X * sin);
    }
}
