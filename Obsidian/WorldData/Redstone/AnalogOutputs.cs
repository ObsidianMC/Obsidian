using Obsidian.API.BlockEntities;
using Obsidian.API.Blocks;
using Obsidian.API.World.Features.Trees;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// What comparators read from vanilla's blocks, like their <c>getAnalogOutputSignal</c>: how full a container is,
/// and the state of cakes, composters, cauldrons, beehives, end portal frames, respawn anchors, copper bulbs, copper
/// golem statues, sculk sensors and command blocks.
/// </summary>
internal static class AnalogOutputs
{
    // Vanilla's Container.LARGE_MAX_STACK_SIZE, the most a container's slot holds.
    private const int ContainerMaxStackSize = 99;

    // Whether each vanilla block, by registry id, has an analog output; asked on every block change.
    private static readonly Lazy<bool[]> withOutput = new(() => Enumerable
        .Range(0, BlocksRegistry.Names.Length)
        .Select(id => HasOutput(BlocksRegistry.GetDefault(id)))
        .ToArray());

    /// <summary>
    /// Vanilla <c>hasAnalogOutputSignal</c> for vanilla's blocks.
    /// </summary>
    /// <remarks>
    /// Creaking hearts have one in vanilla too, which reads as 0 here: a heart's is how near its creaking is, and
    /// Obsidian's hearts don't tick, so they have none.
    /// </remarks>
    public static bool Has(IBlock state)
    {
        var table = withOutput.Value;
        var id = state.RegistryId;

        return (uint)id < (uint)table.Length && table[id];
    }

    private static bool HasOutput(IBlock state) => state.BlockClass() switch
    {
        "ChestBlock" or "TrappedChestBlock" or "BarrelBlock" or "HopperBlock" or "DispenserBlock" or "DropperBlock"
            or "FurnaceBlock" or "BlastFurnaceBlock" or "SmokerBlock" or "BrewingStandBlock" or "ShulkerBoxBlock"
            or "CopperChestBlock" or "WeatheringCopperChestBlock" => true,
        "CakeBlock" or "CandleCakeBlock" or "ComposterBlock" or "CauldronBlock" or "LayeredCauldronBlock"
            or "LavaCauldronBlock" or "BeehiveBlock" or "EndPortalFrameBlock" or "RespawnAnchorBlock" => true,
        "JukeboxBlock" or "LecternBlock" or "ChiseledBookShelfBlock" or "CrafterBlock" or "DecoratedPotBlock"
            or "ShelfBlock" or "CommandBlock" or "SculkSensorBlock" or "CalibratedSculkSensorBlock"
            or "DetectorRailBlock" or "CreakingHeartBlock" or "CopperBulbBlock" or "WeatheringCopperBulbBlock"
            or "CopperGolemStatueBlock" or "WeatheringCopperGolemStatueBlock" => true,
        _ => false
    };

    /// <summary>
    /// Vanilla <c>getAnalogOutputSignal</c> of a vanilla block that has one, read towards <paramref name="direction"/>
    /// (from the block to the comparator reading it), which only shelves care about.
    /// </summary>
    public static int Get(LevelBlockUpdates level, IBlock state, Vector position, BlockFace direction) =>
        state.BlockClass() switch
        {
            "CakeBlock" => (7 - IntProperty(state, "bites")) * 2,
            "CandleCakeBlock" => 14,
            "ComposterBlock" => IntProperty(state, "level"),
            "LayeredCauldronBlock" => IntProperty(state, "level"),
            "LavaCauldronBlock" => 3,
            "BeehiveBlock" => IntProperty(state, "honey_level"),
            "EndPortalFrameBlock" => state.GetProperty("eye") == "true" ? 15 : 0,

            // Vanilla's getScaledChargeLevel: the charges' share of 4, of 15, rounded down.
            "RespawnAnchorBlock" => (int)MathF.Floor(IntProperty(state, "charges") / 4f * 15f),
            "ChestBlock" or "TrappedChestBlock" or "CopperChestBlock" or "WeatheringCopperChestBlock" =>
                ChestSignal(level, state, position),
            "LecternBlock" => level.GetBlockEntity(position) is LecternBlockEntity lectern ? lectern.AnalogSignal() : 0,
            "CrafterBlock" => level.GetBlockEntity(position) is CrafterBlockEntity crafter ? crafter.AnalogSignal() : 0,
            "ChiseledBookShelfBlock" => level.GetBlockEntity(position) is ChiseledBookshelfBlockEntity shelf
                ? shelf.AnalogSignal()
                : 0,
            "ShelfBlock" => level.GetBlockEntity(position) is ShelfBlockEntity row
                ? row.AnalogSignal(direction, state.GetProperty("facing"))
                : 0,
            "JukeboxBlock" => level.GetBlockEntity(position) is DataBlockEntity jukebox
                ? JukeboxSongsRegistry.ComparatorOutput(jukebox)
                : 0,
            "DetectorRailBlock" => DetectorSignal(level, state, position),
            "CopperBulbBlock" or "WeatheringCopperBulbBlock" => state.GetProperty("lit") == "true" ? 15 : 0,

            // Vanilla's pose ordinal plus one: standing, sitting, running, star.
            "CopperGolemStatueBlock" or "WeatheringCopperGolemStatueBlock" => StatuePose(state) + 1,
            "SculkSensorBlock" or "CalibratedSculkSensorBlock" => SculkSignal(level, state, position),
            "CommandBlock" => level.GetBlockEntity(position) is CommandBlockEntity commandBlock
                ? commandBlock.Command.SuccessCount
                : 0,
            _ => ContainerSignal(level, level.GetBlockEntity(position))
        };

    // Vanilla CopperGolemStatueBlock.Pose, in declaration order.
    private static int StatuePose(IBlock state) => state.GetProperty("copper_golem_pose") switch
    {
        "sitting" => 1,
        "running" => 2,
        "star" => 3,
        _ => 0
    };

    // Vanilla SculkSensorBlock: an active sensor gives the frequency of the vibration it last heard.
    private static int SculkSignal(LevelBlockUpdates level, IBlock state, Vector position)
    {
        if (state.GetProperty("sculk_sensor_phase") != "active")
            return 0;

        return SavedNumber(level.GetBlockEntity(position), "last_vibration_frequency");
    }

    // A whole number vanilla saves in the block entity's data (a command block's SuccessCount), 0 when missing.
    private static int SavedNumber(IBlockEntity? blockEntity, string name)
    {
        if (blockEntity is not DataBlockEntity data)
            return 0;

        lock (data.Data)
            return data.Data.TryGetNumber<int>(name, out var value) ? value : 0;
    }

    // A detector rail's signal, from the minecarts in its box.
    private static int DetectorSignal(LevelBlockUpdates level, IBlock state, Vector position)
    {
        var (min, max) = RedstoneDetectorRail.MinecartBox(position);
        return RedstoneDetectorRail.AnalogOutput(state, level.EntitiesIn(min, max));
    }

    /// <summary>
    /// Vanilla <c>getRedstoneSignalFromContainer</c>: 0 when empty, else 1 to 15 by how full the slots are on
    /// average, each slot by its item's stack size.
    /// </summary>
    public static int ContainerSignal(params IEnumerable<BaseContainer> containers)
    {
        var size = 0;
        var fullness = 0f;
        foreach (var container in containers)
        {
            size += container.Size;
            for (var slot = 0; slot < container.Size; slot++)
            {
                if (container[slot] is not { IsAir: false, Count: > 0 } stack)
                    continue;

                fullness += stack.Count / (float)Math.Min(ContainerMaxStackSize, stack.MaxStackSize);
            }
        }

        if (size == 0)
            return 0;

        // Vanilla's Mth.lerpDiscrete(fullness, 0, 15).
        fullness /= size;
        return (int)MathF.Floor(fullness * 14f) + (fullness > 0 ? 1 : 0);
    }

    private static int ContainerSignal(LevelBlockUpdates level, IBlockEntity? blockEntity) =>
        blockEntity is ContainerBlockEntity container ? ContainerSignal(level.ItemsOf(container)) : 0;

    // Vanilla ChestBlock.getContainer: both halves of a double chest, and nothing when either half is blocked by a
    // conductor or a sitting cat above it.
    private static int ChestSignal(LevelBlockUpdates level, IBlock state, Vector position)
    {
        if (IsBlocked(level, position) || level.GetBlockEntity(position) is not ContainerBlockEntity chest)
            return 0;

        if (PartnerOf(state, position) is Vector partner)
        {
            if (IsBlocked(level, partner))
                return 0;

            // Vanilla's double chest reads the right half's slots first.
            if (level.GetBlockEntity(partner) is ContainerBlockEntity other)
            {
                var (first, second) = state.GetProperty("type") == "right" ? (chest, other) : (other, chest);
                return ContainerSignal(level.ItemsOf(first), level.ItemsOf(second));
            }
        }

        return ContainerSignal(level.ItemsOf(chest));
    }

    // Vanilla ChestBlock.getConnectedDirection: a left half's partner is clockwise of its facing, a right half's
    // counterclockwise.
    private static Vector? PartnerOf(IBlock state, Vector position)
    {
        var facing = FeatureHelpers.ParseFace(state.GetProperty("facing"));
        return state.GetProperty("type") switch
        {
            "left" => position.Offset(FeatureHelpers.ClockWise(facing)),
            "right" => position.Offset(FeatureHelpers.CounterClockWise(facing)),
            _ => null
        };
    }

    // Vanilla ChestBlock.isChestBlockedAt: a conductor above, or a cat sitting in the block above.
    private static bool IsBlocked(LevelBlockUpdates level, Vector position)
    {
        if (level.GetBlock(position + Vector.Up).IsRedstoneConductor())
            return true;

        var min = (VectorD)(position + Vector.Up);
        var max = min + new VectorD(1, 1, 1);
        return level.EntitiesIn(min, max).Any(entity => entity is Cat { IsRemoved: false, IsSitting: true });
    }

    private static int IntProperty(IBlock state, string name) =>
        int.TryParse(state.GetProperty(name), out var value) ? value : 0;
}
