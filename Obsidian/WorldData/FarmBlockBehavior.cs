using Microsoft.Extensions.Logging;
using Obsidian.API.Blocks;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Entities.AI;
using Obsidian.Registries;
using Obsidian.WorldData.Fluids;

namespace Obsidian.WorldData;

/// <summary>
/// Vanilla's <c>FarmBlock</c>: its <c>updateShape</c>, <c>tick</c> and <c>randomTick</c> overrides. Farmland dries
/// out and turns back to dirt without water nearby (unless something grows on it), is soaked by water within four
/// blocks, and turns to dirt when a solid block is above it.
/// </summary>
/// <remarks>
/// Not ported: <c>Block.pushEntitiesUp</c> when it turns to dirt, and <c>fallOn</c> trampling, which wait on a landing
/// hook for entities.
/// </remarks>
internal sealed partial class FarmBlockBehavior : IRandomTickBehavior, IScheduledTickBehavior, IShapeUpdateBehavior
{
    // Vanilla's FarmBlock.MAX_MOISTURE.
    private const int MaxMoisture = 7;

    /// <summary>
    /// Vanilla's <c>FarmBlock.updateShape</c>: a block that can't stay (a solid one above it) is scheduled to turn to
    /// dirt a tick later. The state is kept.
    /// </summary>
    public IBlock? UpdateShape(IBlockLevel level, Vector position, IBlock state, BlockFace direction, Vector neighborPosition, IBlock neighbor)
    {
        if (direction == BlockFace.Up && !CanSurvive(level, position))
            level.ScheduleTick(position, state, 1);

        return null;
    }

    /// <summary>Vanilla's <c>FarmBlock.tick</c>.</summary>
    public void Tick(IBlockLevel level, Vector position, IBlock block, IRandomSource random)
    {
        if (!CanSurvive(level, position))
            TurnToDirt(level, position, block);
    }

    /// <summary>Vanilla's <c>FarmBlock.randomTick</c>: no draws.</summary>
    public void RandomTick(AbstractLevel level, Vector position, IBlock block, IRandomSource random)
    {
        var blocks = level.BlockUpdates;
        var moisture = Moisture(block);

        if (!IsNearWater(blocks, position) && !new MobTerrain(level).IsRainingAt(position + Vector.Up))
        {
            if (moisture > 0)
            {
                blocks.SetBlock(position, block.WithProperty("moisture", (moisture - 1).ToString()), BlockUpdateFlags.Clients);
                Log.Moisture(level.Logger, position, moisture, moisture - 1);
            }
            else if (!ShouldMaintainFarmland(blocks, position))
            {
                TurnToDirt(blocks, position, block);
            }
        }
        else if (moisture < MaxMoisture)
        {
            blocks.SetBlock(position, block.WithProperty("moisture", MaxMoisture.ToString()), BlockUpdateFlags.Clients);
            Log.Moisture(level.Logger, position, moisture, MaxMoisture);
        }
    }

    private static int Moisture(IBlock block) => int.TryParse(block.GetProperty("moisture"), out var moisture) ? moisture : 0;

    // Vanilla's FarmBlock.canSurvive: anything above that isn't solid, or is a fence gate or moving piston.
    private static bool CanSurvive(IBlockLevel level, Vector position)
    {
        var above = level.GetBlock(position + Vector.Up);
        return !above.IsSolid() || above.BlockClass() is "MovingPistonBlock" or "FenceGateBlock";
    }

    // Vanilla's FarmBlock.shouldMaintainFarmland: the block above is in the maintains_farmland tag (crops and stems).
    private static bool ShouldMaintainFarmland(IBlockLevel level, Vector position) =>
        TagsRegistry.Block.MaintainsFarmland.Entries.Contains(level.GetBlock(position + Vector.Up).RegistryId);

    // Vanilla's FarmBlock.isNearWater: any water fluid in the box from 4 blocks away horizontally, at the farmland's
    // level and one above.
    private static bool IsNearWater(IBlockLevel level, Vector position)
    {
        for (var x = -4; x <= 4; x++)
        {
            for (var y = 0; y <= 1; y++)
            {
                for (var z = -4; z <= 4; z++)
                {
                    if (FluidState.Of(level.GetBlock(position + new Vector(x, y, z))).IsWater)
                        return true;
                }
            }
        }

        return false;
    }

    // Vanilla's FarmBlock.turnToDirt, without pushing entities up.
    private static void TurnToDirt(IBlockLevel level, Vector position, IBlock block)
    {
        level.SetBlock(position, BlocksRegistry.Dirt, BlockUpdateFlags.All);
        Log.ToDirt(((AbstractLevel)level.Level).Logger, position, Moisture(block));
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Farmland at {Position} changed moisture from {From} to {To}.")]
        public static partial void Moisture(ILogger logger, Vector position, int from, int to);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Farmland at {Position} with moisture {Moisture} turned to dirt.")]
        public static partial void ToDirt(ILogger logger, Vector position, int moisture);
    }
}
