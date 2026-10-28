namespace Obsidian.WorldData.Features;

/// <summary>
/// Vanilla <c>BlockState.canSurvive(level, pos)</c> for the blocks that world generation features place.
/// </summary>
/// <remarks>
/// Rules are keyed by the vanilla block class (<see cref="BlockPhysics.BlockClass"/>); blocks whose class doesn't override
/// <c>canSurvive</c> always survive. Light is evaluated like vanilla during feature placement, where chunks are not lit yet:
/// sky light reads 15 everywhere. Live shape updates supply the actual raw brightness instead.
/// A few vanilla checks use shapes Obsidian doesn't have (collision face shapes, center support); those use the closest
/// sturdy-face data and are called out below.
/// </remarks>
internal static class BlockSurvival
{
    private static readonly BlockSet dirt = new("#minecraft:dirt");
    private static readonly BlockSet sand = new("#minecraft:sand");
    private static readonly BlockSet mushroomGrowBlock = new("#minecraft:mushroom_grow_block");
    private static readonly BlockSet dryVegetationMayPlaceOn = new("#minecraft:dry_vegetation_may_place_on");
    private static readonly BlockSet smallDripleafPlaceable = new("#minecraft:small_dripleaf_placeable");
    private static readonly BlockSet bigDripleafPlaceable = new("#minecraft:big_dripleaf_placeable");
    private static readonly BlockSet bambooPlantableOn = new("#minecraft:bamboo_plantable_on");
    internal static readonly BlockSet JungleLogs = new("#minecraft:jungle_logs");
    private static readonly BlockSet snowLayerCannotSurviveOn = new("#minecraft:snow_layer_cannot_survive_on");
    private static readonly BlockSet snowLayerCanSurviveOn = new("#minecraft:snow_layer_can_survive_on");
    private static readonly BlockSet nylium = new("#minecraft:nylium");

    /// <summary>
    /// Whether <paramref name="block"/> could stay at <paramref name="position"/>, like vanilla <c>BlockState.canSurvive</c>.
    /// </summary>
    public static bool CanSurvive(
        this IBlock block, IWorldGenLevel level, Vector position, int rawBrightness = 15) => block.BlockClass() switch
    {
        "FlowerBlock" or "TallGrassBlock" or "SweetBerryBushBlock" or "BushBlock" or "FireflyBushBlock" or "FlowerBedBlock"
            or "EyeblossomBlock" or "SaplingBlock" or "VegetationBlock" => MayPlaceOnVegetation(Below(level, position)),
        "WitherRoseBlock" => WitherRoseMayPlaceOn(Below(level, position)),
        "AzaleaBlock" => Below(level, position).Is(BlockIds.Clay) || MayPlaceOnVegetation(Below(level, position)),
        "MangrovePropaguleBlock" => MangrovePropaguleCanSurvive(block, level, position),
        "NetherFungusBlock" => FungusMayPlaceOn(Below(level, position)),
        "NetherRootsBlock" or "NetherSproutsBlock" => NetherPlantMayPlaceOn(Below(level, position)),
        "DoublePlantBlock" or "TallFlowerBlock" => DoublePlantCanSurvive(block, level, position, MayPlaceOnVegetation),
        "TallSeagrassBlock" => TallSeagrassCanSurvive(block, level, position),
        "SeagrassBlock" => SeagrassMayPlaceOn(Below(level, position)),
        "SmallDripleafBlock" => SmallDripleafCanSurvive(block, level, position),
        "DryVegetationBlock" or "ShortDryGrassBlock" or "TallDryGrassBlock" => dryVegetationMayPlaceOn.Contains(Below(level, position)),
        "MushroomBlock" => MushroomCanSurvive(level, position, rawBrightness),
        "CactusBlock" => CactusMayStay(level.GetBlock, position),
        "CactusFlowerBlock" => CactusFlowerMayPlaceOn(Below(level, position)),
        "SugarCaneBlock" => SugarCaneMayStay(level.GetBlock, position),
        "BaseCoralPlantBlock" or "CoralPlantBlock" or "BaseCoralFanBlock" or "CoralFanBlock" =>
            Below(level, position).IsFaceSturdy(BlockFace.Up),
        "BaseCoralWallFanBlock" or "CoralWallFanBlock" or "AmethystClusterBlock" => AttachedBehindCanSurvive(block, level, position, "facing"),
        "KelpBlock" or "KelpPlantBlock" => GrowingPlantCanSurvive(level, position, BlockFace.Up,
            BlockIds.Kelp, BlockIds.KelpPlant, attachTo => !attachTo.Is(BlockIds.MagmaBlock)),
        "CaveVinesBlock" or "CaveVinesPlantBlock" => GrowingPlantCanSurvive(level, position, BlockFace.Down,
            BlockIds.CaveVines, BlockIds.CaveVinesPlant, _ => true),
        "WeepingVinesBlock" or "WeepingVinesPlantBlock" => GrowingPlantCanSurvive(level, position, BlockFace.Down,
            BlockIds.WeepingVines, BlockIds.WeepingVinesPlant, _ => true),
        "TwistingVinesBlock" or "TwistingVinesPlantBlock" => GrowingPlantCanSurvive(level, position, BlockFace.Up,
            BlockIds.TwistingVines, BlockIds.TwistingVinesPlant, _ => true),
        "SeaPickleBlock" => SeaPickleMayPlaceOn(Below(level, position)),
        "BambooStalkBlock" or "BambooSaplingBlock" => bambooPlantableOn.Contains(Below(level, position)),
        "CocoaBlock" => CocoaCanSurvive(block, level, position),
        "CarpetBlock" or "WoolCarpetBlock" => !Below(level, position).IsAir,
        "MossyCarpetBlock" => MossyCarpetCanSurvive(block, level, position),
        "BigDripleafBlock" => BigDripleafCanSurvive(level, position),
        "BigDripleafStemBlock" => BigDripleafStemCanSurvive(level, position),
        "SporeBlossomBlock" => SporeBlossomCanSurvive(level, position),
        "HangingRootsBlock" => level.GetBlock(position + Vector.Up).IsFaceSturdy(BlockFace.Down),
        "HangingMossBlock" => HangingMossCanSurvive(level, position),
        "SnowLayerBlock" => SnowLayerCanSurvive(level, position),
        "PointedDripstoneBlock" => PointedDripstoneCanSurvive(block, level, position),
        "MultifaceBlock" or "GlowLichenBlock" or "SculkVeinBlock" => MultifaceCanSurvive(block, level, position),
        "VineBlock" => VineCanSurvive(block, level, position),
        "LilyPadBlock" => WaterlilyCanSurvive(level, position),
        "LeafLitterBlock" => Below(level, position).IsFaceSturdy(BlockFace.Up),
        "TorchBlock" or "RedstoneTorchBlock" => Below(level, position).IsTopCenterSturdy(),
        "StemBlock" or "AttachedStemBlock" or "CropBlock" or "CarrotBlock" or "PotatoBlock" or "BeetrootBlock" =>
            Below(level, position).RegistryId == BlockIds.Farmland,
        _ => true
    };

    /// <summary>
    /// Vanilla <c>MultifaceBlock.canAttachTo</c>: the neighbor in <paramref name="direction"/> has a full face toward us.
    /// </summary>
    /// <remarks>
    /// Vanilla accepts a full support shape face or a full collision shape face. Obsidian has the support side
    /// (<see cref="BlockPhysics.IsFaceSturdy"/>) but only knows whether the collision shape is a full cube, which still
    /// covers leaves (no support shape, full collision).
    /// </remarks>
    public static bool CanAttachTo(IBlock neighbor, BlockFace direction) =>
        neighbor.IsFaceSturdy(direction.Opposite()) || neighbor.IsCollisionShapeFullBlock();

    private static IBlock Below(IWorldGenLevel level, Vector position) => level.GetBlock(position + Vector.Down);

    private static bool IsWater(IBlock block) => block.GetFluid() is FluidKind.Water or FluidKind.FlowingWater;

    // VegetationBlock.mayPlaceOn: dirt-tagged blocks and farmland.
    private static bool MayPlaceOnVegetation(IBlock below) => dirt.Contains(below) || below.Is(BlockIds.Farmland);

    private static bool WitherRoseMayPlaceOn(IBlock below) =>
        MayPlaceOnVegetation(below)
            || below.RegistryId is BlockIds.Netherrack or BlockIds.SoulSand or BlockIds.SoulSoil;

    private static bool FungusMayPlaceOn(IBlock below) =>
        nylium.Contains(below)
            || below.RegistryId is BlockIds.Mycelium or BlockIds.SoulSoil
            || MayPlaceOnVegetation(below);

    private static bool NetherPlantMayPlaceOn(IBlock below) =>
        nylium.Contains(below) || below.Is(BlockIds.SoulSoil) || MayPlaceOnVegetation(below);

    private static bool MangrovePropaguleCanSurvive(IBlock block, IWorldGenLevel level, Vector position)
    {
        if (block.GetProperty("hanging") == "true")
            return level.GetBlock(position + Vector.Up).RegistryId == BlockIds.MangroveLeaves;

        var below = Below(level, position);
        return MayPlaceOnVegetation(below) || below.Is(BlockIds.Clay);
    }

    // DoublePlantBlock: the upper half needs the matching lower half below it.
    private static bool DoublePlantCanSurvive(IBlock block, IWorldGenLevel level, Vector position, Func<IBlock, bool> mayPlaceOn)
    {
        if (block.GetProperty("half") == "upper")
            return IsLowerHalfOf(Below(level, position), block);

        return mayPlaceOn(Below(level, position));
    }

    private static bool IsLowerHalfOf(IBlock below, IBlock block) =>
        below.RegistryId == block.RegistryId && below.GetProperty("half") == "lower";

    private static bool SeagrassMayPlaceOn(IBlock below) =>
        below.IsFaceSturdy(BlockFace.Up) && !below.Is(BlockIds.MagmaBlock);

    private static bool TallSeagrassCanSurvive(IBlock block, IWorldGenLevel level, Vector position)
    {
        if (block.GetProperty("half") == "upper")
            return IsLowerHalfOf(Below(level, position), block);

        var here = level.GetBlock(position);
        return SeagrassMayPlaceOn(Below(level, position)) && IsWater(here) && here.FluidAmount() == 8;
    }

    private static bool SmallDripleafCanSurvive(IBlock block, IWorldGenLevel level, Vector position)
    {
        if (block.GetProperty("half") == "upper")
            return IsLowerHalfOf(Below(level, position), block);

        // SmallDripleafBlock.mayPlaceOn: its tag, or ordinary vegetation ground under a water source.
        var below = Below(level, position);
        return smallDripleafPlaceable.Contains(below)
            || level.GetBlock(position).GetFluid() == FluidKind.Water && MayPlaceOnVegetation(below);
    }

    private static bool MushroomCanSurvive(IWorldGenLevel level, Vector position, int rawBrightness)
    {
        var below = Below(level, position);
        if (mushroomGrowBlock.Contains(below))
            return true;

        // Vanilla MushroomBlock.canSurvive uses raw light with zero sky darkening, even at night.
        return rawBrightness < 13 && below.IsSolid() && below.IsCollisionShapeFullBlock();
    }

    /// <summary>Vanilla <c>CactusBlock.canSurvive</c>, reading blocks through <paramref name="blockAt"/> (worldgen or a live level).</summary>
    internal static bool CactusMayStay(Func<Vector, IBlock> blockAt, Vector position)
    {
        foreach (var face in FeatureHelpers.Horizontal)
        {
            var neighbor = blockAt(position.Offset(face));
            if (neighbor.IsSolid() || neighbor.GetFluid() is FluidKind.Lava or FluidKind.FlowingLava)
                return false;
        }

        var below = blockAt(position + Vector.Down);
        return (below.Is(BlockIds.Cactus) || sand.Contains(below)) && !blockAt(position + Vector.Up).IsLiquid;
    }

    /// <summary>Vanilla <c>BambooStalkBlock.canSurvive</c>: the block beneath is on the bamboo-plantable tag.</summary>
    internal static bool BambooMayStay(Func<Vector, IBlock> blockAt, Vector position) =>
        bambooPlantableOn.Contains(blockAt(position + Vector.Down));

    private static bool CactusFlowerMayPlaceOn(IBlock below) =>
        below.RegistryId is BlockIds.Cactus or BlockIds.Farmland || below.IsTopCenterSturdy();

    /// <summary>Vanilla <c>SugarCaneBlock.canSurvive</c>, reading blocks through <paramref name="blockAt"/> (worldgen or a live level).</summary>
    internal static bool SugarCaneMayStay(Func<Vector, IBlock> blockAt, Vector position)
    {
        var below = blockAt(position + Vector.Down);
        if (below.Is(BlockIds.SugarCane))
            return true;

        if (!dirt.Contains(below) && !sand.Contains(below))
            return false;

        var belowPosition = position + Vector.Down;
        foreach (var face in FeatureHelpers.Horizontal)
        {
            var neighbor = blockAt(belowPosition.Offset(face));
            if (IsWater(neighbor) || neighbor.Is(BlockIds.FrostedIce))
                return true;
        }

        return false;
    }

    // Blocks attached to the block behind their "facing" property (wall fans, amethyst buds).
    private static bool AttachedBehindCanSurvive(IBlock block, IWorldGenLevel level, Vector position, string property)
    {
        var facing = FeatureHelpers.ParseFace(block.GetProperty(property));
        return level.GetBlock(position.Offset(facing.Opposite())).IsFaceSturdy(facing);
    }

    // GrowingPlantBlock: attached to the block opposite its growth direction.
    private static bool GrowingPlantCanSurvive(IWorldGenLevel level, Vector position, BlockFace growth, int head,
        int body, Func<IBlock, bool> canAttachTo)
    {
        var attached = level.GetBlock(position.Offset(growth.Opposite()));
        if (!canAttachTo(attached))
            return false;

        return attached.RegistryId == head || attached.RegistryId == body || attached.IsFaceSturdy(growth);
    }

    /// <remarks>
    /// Vanilla also accepts any non-empty top collision face (e.g. slabs on top); the center/full sturdy checks cover the
    /// blocks found on ocean floors.
    /// </remarks>
    private static bool SeaPickleMayPlaceOn(IBlock below) => below.IsFaceSturdy(BlockFace.Up) || below.IsTopCenterSturdy();

    private static bool CocoaCanSurvive(IBlock block, IWorldGenLevel level, Vector position)
    {
        var facing = FeatureHelpers.ParseFace(block.GetProperty("facing"));
        return JungleLogs.Contains(level.GetBlock(position.Offset(facing)));
    }

    private static bool MossyCarpetCanSurvive(IBlock block, IWorldGenLevel level, Vector position)
    {
        var below = Below(level, position);
        if (block.GetProperty("bottom") == "true")
            return !below.IsAir;

        return below.RegistryId == block.RegistryId && below.GetProperty("bottom") == "true";
    }

    private static bool BigDripleafCanSurvive(IWorldGenLevel level, Vector position)
    {
        var below = Below(level, position);
        return below.RegistryId is BlockIds.BigDripleaf or BlockIds.BigDripleafStem
            || bigDripleafPlaceable.Contains(below);
    }

    private static bool BigDripleafStemCanSurvive(IWorldGenLevel level, Vector position)
    {
        var below = Below(level, position);
        var above = level.GetBlock(position + Vector.Up);
        return (below.Is(BlockIds.BigDripleafStem) || bigDripleafPlaceable.Contains(below))
            && above.RegistryId is BlockIds.BigDripleafStem or BlockIds.BigDripleaf;
    }

    /// <remarks>
    /// Vanilla needs center support on the bottom face of the block above; a full sturdy bottom face (cave ceilings) implies it.
    /// </remarks>
    private static bool SporeBlossomCanSurvive(IWorldGenLevel level, Vector position) =>
        level.GetBlock(position + Vector.Up).IsFaceSturdy(BlockFace.Down) && !IsWater(level.GetBlock(position));

    private static bool HangingMossCanSurvive(IWorldGenLevel level, Vector position)
    {
        var above = level.GetBlock(position + Vector.Up);
        return CanAttachTo(above, BlockFace.Up) || above.Is(BlockIds.PaleHangingMoss);
    }

    /// <remarks>
    /// The "full top collision face" check uses the sturdy top face, which matches for full and partial blocks alike.
    /// </remarks>
    private static bool SnowLayerCanSurvive(IWorldGenLevel level, Vector position)
    {
        var below = Below(level, position);
        if (snowLayerCannotSurviveOn.Contains(below))
            return false;

        if (snowLayerCanSurviveOn.Contains(below))
            return true;

        return below.IsFaceSturdy(BlockFace.Up) || below.Is(BlockIds.Snow) && below.GetProperty("layers") == "8";
    }

    private static bool PointedDripstoneCanSurvive(IBlock block, IWorldGenLevel level, Vector position)
    {
        var tip = FeatureHelpers.ParseFace(block.GetProperty("vertical_direction"));
        var behind = level.GetBlock(position.Offset(tip.Opposite()));
        return behind.IsFaceSturdy(tip)
            || behind.Is(BlockIds.PointedDripstone)
                && behind.GetProperty("vertical_direction") == block.GetProperty("vertical_direction");
    }

    private static bool MultifaceCanSurvive(IBlock block, IWorldGenLevel level, Vector position)
    {
        var hasFace = false;
        foreach (var face in FeatureHelpers.Directions)
        {
            if (block.GetProperty(FeatureHelpers.FaceName(face)) != "true")
                continue;

            if (!CanAttachTo(level.GetBlock(position.Offset(face)), face))
                return false;

            hasFace = true;
        }

        return hasFace;
    }

    // VineBlock.canSurvive: some face is still supported after VineBlock.getUpdatedState.
    private static bool VineCanSurvive(IBlock block, IWorldGenLevel level, Vector position)
    {
        if (block.GetProperty("up") == "true" && CanAttachTo(level.GetBlock(position + Vector.Up), BlockFace.Up))
            return true;

        IBlock? above = null;
        foreach (var face in FeatureHelpers.Horizontal)
        {
            var name = FeatureHelpers.FaceName(face);
            if (block.GetProperty(name) != "true")
                continue;

            if (CanAttachTo(level.GetBlock(position.Offset(face)), face))
                return true;

            above ??= level.GetBlock(position + Vector.Up);
            if (above.RegistryId == block.RegistryId && above.GetProperty(name) == "true")
                return true;
        }

        return false;
    }

    private static bool WaterlilyCanSurvive(IWorldGenLevel level, Vector position)
    {
        var below = Below(level, position);
        return (below.GetFluid() == FluidKind.Water || below.BlockClass() is "IceBlock" or "FrostedIceBlock") && !level.GetBlock(position).HasFluid();
    }
}
