using Obsidian.API.Blocks;
using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData;

/// <summary>
/// Vanilla <c>BlockState.canSurvive</c> for a block a player places: <c>BlockItem.canPlace</c> refuses a block that
/// wouldn't survive where it goes, and attached blocks (torches, ladders, wall signs) try each side until one holds.
/// </summary>
/// <remarks>
/// Rules are keyed by the vanilla block class (<see cref="BlockPhysics.BlockClass"/>). Blocks this doesn't name take
/// the rules world generation uses (<see cref="BlockSurvival"/>), with the level's actual light; any other block
/// survives anywhere.
/// </remarks>
internal static class PlacementSurvival
{
    private static readonly WorldgenTags.BlockGroup unstableBottomCenter = WorldgenTags.Blocks("unstable_bottom_center");
    private static readonly WorldgenTags.BlockGroup supportsCrops = WorldgenTags.Blocks("supports_crops");
    private static readonly WorldgenTags.BlockGroup supportsFrogspawn = WorldgenTags.Blocks("supports_frogspawn");
    private static readonly WorldgenTags.BlockGroup supportsNetherWart = WorldgenTags.Blocks("supports_nether_wart");
    private static readonly WorldgenTags.BlockGroup supportsChorusPlant = WorldgenTags.Blocks("supports_chorus_plant");
    private static readonly WorldgenTags.BlockGroup supportsChorusFlower = WorldgenTags.Blocks("supports_chorus_flower");
    private static readonly WorldgenTags.BlockGroup supportsMelonStem = WorldgenTags.Blocks("supports_melon_stem");
    private static readonly WorldgenTags.BlockGroup supportsPumpkinStem = WorldgenTags.Blocks("supports_pumpkin_stem");

    /// <summary>
    /// Whether <paramref name="state"/> would survive at <paramref name="position"/>, like vanilla's
    /// <c>canSurvive</c>.
    /// </summary>
    public static bool CanSurvive(IBlockLevel level, IBlock state, Vector position)
    {
        switch (state.BlockClass())
        {
            case "TorchBlock" or "RedstoneTorchBlock" or "CandleBlock":
                return SupportsCenter(level, position + Vector.Down, BlockFace.Up);
            case "WallTorchBlock" or "RedstoneWallTorchBlock" or "LadderBlock" or "TripWireHookBlock"
                or "ShelfMushroomBlock" or "BaseCoralWallFanBlock" or "CoralWallFanBlock" or "AmethystClusterBlock":
                return SturdyBehind(level, state, position);
            case "LanternBlock" or "WeatheringLanternBlock":
                return state.GetProperty("hanging") == "true"
                    ? SupportsCenter(level, position + Vector.Up, BlockFace.Down)
                    : SupportsCenter(level, position + Vector.Down, BlockFace.Up);
            case "PressurePlateBlock" or "WeightedPressurePlateBlock":
                return SupportsRigid(level, position + Vector.Down)
                    || SupportsCenter(level, position + Vector.Down, BlockFace.Up);
            case "ComparatorBlock" or "RepeaterBlock" or "RailBlock" or "PoweredRailBlock" or "DetectorRailBlock":
                return SupportsRigid(level, position + Vector.Down);
            case "RedstoneWireBlock":
                return Redstone.WireConnections.CanStandOn(level.GetBlock(position + Vector.Down));
            case "BannerBlock" or "StandingSignBlock" or "CakeBlock" or "CandleCakeBlock":
                return level.GetBlock(position + Vector.Down).IsSolid();
            case "WallBannerBlock" or "WallSignBlock":
                return level.GetBlock(position.Offset(Facing(state).Opposite())).IsSolid();
            case "CeilingHangingSignBlock":
                return level.GetBlock(position + Vector.Up).IsFaceSturdy(BlockFace.Down, SupportType.Center);
            case "LeverBlock" or "ButtonBlock":
                return CanAttach(level, position, Redstone.BasicRedstoneComponents.AttachedDirection(state));
            case "BellBlock":
                return BellCanSurvive(level, state, position);
            case "BaseCoralPlantBlock" or "CoralPlantBlock" or "BaseCoralFanBlock" or "CoralFanBlock"
                or "LeafLitterBlock":
                return level.GetBlock(position + Vector.Down).IsFaceSturdy(BlockFace.Up);
            case "DoorBlock" or "WeatheringCopperDoorBlock":
                return state.GetProperty("half") == "upper"
                    ? level.GetBlock(position + Vector.Down).RegistryId == state.RegistryId
                    : level.GetBlock(position + Vector.Down).IsFaceSturdy(BlockFace.Up);
            case "FrogspawnBlock":
                // Vanilla FrogspawnBlock.mayPlaceOn: on a water source (the supports_frogspawn fluid tag) or a block
                // tagged to hold it, with nothing liquid where it goes.
                var underSpawn = level.GetBlock(position + Vector.Down);
                return (underSpawn.GetFluid() == FluidKind.Water || supportsFrogspawn.Contains(underSpawn))
                    && !level.GetBlock(position).HasFluid();
            case "NetherWartBlock":
                return supportsNetherWart.Contains(level.GetBlock(position + Vector.Down));
            case "ChorusPlantBlock":
                return ChorusPlantCanSurvive(level, state, position);
            case "ChorusFlowerBlock":
                return ChorusFlowerCanSurvive(level, position);
            case "PitcherCropBlock":
                return state.GetProperty("half") == "upper"
                    ? level.GetBlock(position + Vector.Down).RegistryId == state.RegistryId
                    : supportsCrops.Contains(level.GetBlock(position + Vector.Down))
                        && RawBrightness(level, position) >= 8;
            case "StemBlock":
                var stemSupport = state.Is(BlockIds.MelonStem) ? supportsMelonStem : supportsPumpkinStem;
                return stemSupport.Contains(level.GetBlock(position + Vector.Down));
            case "CropBlock" or "CarrotBlock" or "PotatoBlock" or "BeetrootBlock" or "TorchflowerCropBlock":
                // CropBlock.hasSufficientLight: a raw brightness of 8.
                return supportsCrops.Contains(level.GetBlock(position + Vector.Down))
                    && RawBrightness(level, position) >= 8;
            case "CocoaBlock":
                return BlockSurvival.JungleLogs.Contains(level.GetBlock(position.Offset(Facing(state))));
            case "SugarCaneBlock":
                return BlockSurvival.SugarCaneMayStay(level.GetBlock, position);
            case "CactusBlock":
                return BlockSurvival.CactusMayStay(level.GetBlock, position);
            case "BambooStalkBlock" or "BambooSaplingBlock":
                return BlockSurvival.BambooMayStay(level.GetBlock, position);
            case "MushroomBlock":
                return state.CanSurvive(new View(level), position, RawBrightness(level, position));
            default:
                return state.CanSurvive(new View(level), position);
        }
    }

    /// <summary>
    /// Vanilla <c>FaceAttachedHorizontalDirectionalBlock.canAttach</c>: the block in <paramref name="direction"/> has a
    /// sturdy face toward the attached block.
    /// </summary>
    public static bool CanAttach(IBlockLevel level, Vector position, BlockFace direction) =>
        level.GetBlock(position.Offset(direction)).IsFaceSturdy(direction.Opposite());

    // Vanilla Block.canSupportCenter: the middle of the face holds, unless it's the bottom of a block that's unstable
    // there (a hanging block can't go under a pointed dripstone, say).
    private static bool SupportsCenter(IBlockLevel level, Vector support, BlockFace face)
    {
        var block = level.GetBlock(support);
        if (face == BlockFace.Down && unstableBottomCenter.Contains(block))
            return false;

        return block.IsFaceSturdy(face, SupportType.Center);
    }

    // Vanilla Block.canSupportRigidBlock: the top face holds a rail or a diode.
    private static bool SupportsRigid(IBlockLevel level, Vector support) =>
        level.GetBlock(support).IsFaceSturdy(BlockFace.Up, SupportType.Rigid);

    // A block facing away from the block behind it, which has a sturdy face toward it.
    private static bool SturdyBehind(IBlockLevel level, IBlock state, Vector position)
    {
        var facing = Facing(state);
        if (facing.IsVertical() && state.BlockClass() == "TripWireHookBlock")
            return false;

        return level.GetBlock(position.Offset(facing.Opposite())).IsFaceSturdy(facing);
    }

    // Vanilla BellBlock.canSurvive: a ceiling bell hangs from the middle of the block above; others attach to the side
    // they connect to: the floor, or the wall the bell faces.
    private static bool BellCanSurvive(IBlockLevel level, IBlock state, Vector position)
    {
        var connection = state.GetProperty("attachment") switch
        {
            "floor" => BlockFace.Down,
            "ceiling" => BlockFace.Up,
            _ => Facing(state)
        };

        return connection == BlockFace.Up
            ? SupportsCenter(level, position + Vector.Up, BlockFace.Down)
            : CanAttach(level, position, connection);
    }

    private static bool ChorusPlantCanSurvive(IBlockLevel level, IBlock state, Vector position)
    {
        var below = level.GetBlock(position + Vector.Down);
        var blockAboveOrBelow = !level.GetBlock(position + Vector.Up).IsAir && !below.IsAir;
        foreach (var side in (BlockFace[])[BlockFace.North, BlockFace.East, BlockFace.South, BlockFace.West])
        {
            var neighborPosition = position.Offset(side);
            if (level.GetBlock(neighborPosition).RegistryId != state.RegistryId)
                continue;

            if (blockAboveOrBelow)
                return false;

            var underNeighbor = level.GetBlock(neighborPosition + Vector.Down);
            if (underNeighbor.RegistryId == state.RegistryId || supportsChorusPlant.Contains(underNeighbor))
                return true;
        }

        return below.RegistryId == state.RegistryId || supportsChorusPlant.Contains(below);
    }

    private static bool ChorusFlowerCanSurvive(IBlockLevel level, Vector position)
    {
        var below = level.GetBlock(position + Vector.Down);
        if (below.Is(BlockIds.ChorusPlant) || supportsChorusFlower.Contains(below))
            return true;

        if (!below.IsAir)
            return false;

        var oneNeighbor = false;
        foreach (var side in (BlockFace[])[BlockFace.North, BlockFace.East, BlockFace.South, BlockFace.West])
        {
            var neighbor = level.GetBlock(position.Offset(side));
            if (neighbor.Is(BlockIds.ChorusPlant))
            {
                if (oneNeighbor)
                    return false;

                oneNeighbor = true;
            }
            else if (!neighbor.IsAir)
            {
                return false;
            }
        }

        return oneNeighbor;
    }

    private static BlockFace Facing(IBlock state) => FeatureHelpers.ParseFace(state.GetProperty("facing"));

    private static int RawBrightness(IBlockLevel level, Vector position) =>
        level.Level is AbstractLevel live ? live.Fluids.GetRawBrightness(position) : 15;

    /// <summary>The level as the rules world generation shares with placement read it: blocks only.</summary>
    private sealed class View(IBlockLevel level) : IWorldGenLevel
    {
        public long Seed => throw new NotSupportedException();

        public int MinY => throw new NotSupportedException();

        public int Height => throw new NotSupportedException();

        public int SeaLevel => throw new NotSupportedException();

        public IRandomSource Random => throw new NotSupportedException();

        public IBlock GetBlock(Vector position) => level.GetBlock(position);

        public bool SetBlock(Vector position, IBlock block) => throw new NotSupportedException();

        public bool EnsureCanWrite(Vector position) => false;

        public int GetHeight(HeightmapType type, int x, int z) => throw new NotSupportedException();

        public BiomeCodec GetBiome(Vector position) => throw new NotSupportedException();

        public void SetBlockEntity(Vector position, IBlockEntity blockEntity) => throw new NotSupportedException();

        public IBlockEntity? GetBlockEntity(Vector position) => level.GetBlockEntity(position);

        public void AddEntity(GeneratedEntity entity) => throw new NotSupportedException();

        public void MarkForPostProcessing(Vector position) => throw new NotSupportedException();

        public void ScheduleFluidTick(Vector position) => throw new NotSupportedException();
    }
}
