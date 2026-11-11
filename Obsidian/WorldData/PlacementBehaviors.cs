using Obsidian.API.Blocks;
using Obsidian.Registries;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Placement;
using Obsidian.WorldData.Redstone;

namespace Obsidian.WorldData;

/// <summary>
/// Obsidian implementations of vanilla classes that override <c>Block.getStateForPlacement</c>. Member names match
/// the Mojang declaring classes dumped by <c>VanillaDumper</c>; the block generator binds blocks directly to them.
/// </summary>
internal static class PlacementBehaviors
{
    private static readonly IBlockBehavior HorizontalOpposite = new HorizontalFacing(opposite: true);
    private static readonly IBlockBehavior NearestOpposite = new NearestFacing();
    private static readonly IBlockBehavior ClickedFace = new ClickedFaceFacing(opposite: false);
    private static readonly IBlockBehavior ClickedFaceOpposite = new ClickedFaceFacing(opposite: true);
    private static readonly IBlockBehavior HorizontalOppositeWaterlogged =
        Placing(BlockPlacements.FacingAwayWaterlogged);
    private static readonly IBlockBehavior Waterlogged = Placing(BlockPlacements.Waterlogged);
    private static readonly IBlockBehavior FullWaterlogged = Placing(BlockPlacements.FullWaterlogged);
    private static readonly IBlockBehavior Pillar = Placing(BlockPlacements.Pillar);
    private static readonly IBlockBehavior Segmented = Placing(BlockPlacements.Segmented);
    private static readonly IBlockBehavior OnWall = Placing(BlockPlacements.OnWall);
    private static readonly IBlockBehavior Leaves = new LeavesBlockBehavior.Placement();
    private static readonly IBlockBehavior BambooStalk = new BambooStalkPlacement();
    private static readonly IBlockBehavior Kelp = new KelpPlacement();
    private static readonly IBlockBehavior Seagrass = new SeagrassPlacement();
    private static readonly IBlockBehavior Crafter = new CrafterPlacement();
    private static readonly IBlockBehavior Rail = new RailPlacement();
    private static readonly IBlockBehavior Diode = new DerivedPlacement(context => RedstoneComparator.DerivePlacement(context.Block, context.Player.Yaw));
    private static readonly IBlockBehavior Repeater = new DerivedPlacement(context => RedstoneRepeater.DerivePlacement(context.Block, context.Player.Yaw));
    private static readonly IBlockBehavior Observer = new DerivedPlacement(context => RedstoneObserver.DerivePlacement(context.Block, context.NearestLookDirection));
    private static readonly IBlockBehavior Piston = new DerivedPlacement(context => Pistons.PistonBehaviors.DerivePlacement(context.Block, context.NearestLookDirection));
    // Vanilla RedstoneLampBlock.getStateForPlacement: lit when the level has a neighbour signal where the lamp goes
    // (vanilla's getClickedPos is the placed position).
    private static readonly IBlockBehavior Lamp = new DerivedPlacement(context =>
        BasicRedstoneComponents.DeriveLampPlacement(context.Block, context.Level.HasNeighborSignal(context.Position)));

    // One member per Mojang class that owns an implemented placement override. Classes may share an implementation
    // object when their current vanilla algorithms are equivalent; block membership comes only from vanilla's class.
    // A block whose class doesn't override getStateForPlacement is placed in its default state.
    internal static IBlockBehavior AbstractBedBlock { get; } = Placing(BlockPlacements.Bed);
    internal static IBlockBehavior AbstractFurnaceBlock => HorizontalOpposite;
    internal static IBlockBehavior AmethystClusterBlock { get; } = Placing(BlockPlacements.AmethystCluster);
    internal static IBlockBehavior AnvilBlock { get; } = Placing(BlockPlacements.Anvil);
    internal static IBlockBehavior BambooStalkBlock => BambooStalk;
    internal static IBlockBehavior BannerBlock { get; } = Placing(BlockPlacements.Banner);
    internal static IBlockBehavior BarrelBlock => NearestOpposite;
    internal static IBlockBehavior BarrierBlock => Waterlogged;
    internal static IBlockBehavior BaseCoralPlantTypeBlock => FullWaterlogged;
    internal static IBlockBehavior BaseCoralWallFanBlock { get; } = Placing(BlockPlacements.CoralWallFan);
    internal static IBlockBehavior BaseRailBlock => Rail;
    internal static IBlockBehavior BeehiveBlock => HorizontalOpposite;
    internal static IBlockBehavior BellBlock { get; } = Placing(BlockPlacements.Bell);
    internal static IBlockBehavior BigDripleafBlock { get; } = Placing(BlockPlacements.BigDripleaf);
    internal static IBlockBehavior CalibratedSculkSensorBlock { get; } = Placing(BlockPlacements.CalibratedSculkSensor);
    internal static IBlockBehavior CampfireBlock { get; } = Placing(BlockPlacements.Campfire);
    internal static IBlockBehavior CandleBlock { get; } = Placing(BlockPlacements.Candle);
    internal static IBlockBehavior CarvedPumpkinBlock => HorizontalOpposite;
    internal static IBlockBehavior CeilingHangingSignBlock { get; } = Placing(BlockPlacements.CeilingHangingSign);
    internal static IBlockBehavior ChainBlock { get; } = Placing(BlockPlacements.Chain);
    internal static IBlockBehavior ChestBlock { get; } = Placing(BlockPlacements.Chest);
    internal static IBlockBehavior ChiseledBookShelfBlock => HorizontalOpposite;
    internal static IBlockBehavior ChorusPlantBlock { get; } = Placing(BlockPlacements.ChorusPlant);
    internal static IBlockBehavior CocoaBlock { get; } = Placing(BlockPlacements.Cocoa);
    internal static IBlockBehavior CommandBlock => NearestOpposite;
    internal static IBlockBehavior ConcretePowderBlock { get; } = Placing(BlockPlacements.ConcretePowder);
    internal static IBlockBehavior ConduitBlock => FullWaterlogged;
    internal static IBlockBehavior CopperChestBlock { get; } = Placing(BlockPlacements.CopperChest);
    internal static IBlockBehavior CopperGolemStatueBlock => HorizontalOppositeWaterlogged;
    // Vanilla CoralBlock schedules its death away from water; Obsidian's coral doesn't die yet, so it only places.
    internal static IBlockBehavior CoralBlock { get; } = Placing(context => context.Block);
    internal static IBlockBehavior CrafterBlock => Crafter;
    internal static IBlockBehavior CreakingHeartBlock => Pillar;
    internal static IBlockBehavior DecoratedPotBlock { get; } = Placing(BlockPlacements.DecoratedPot);
    internal static IBlockBehavior DispenserBlock => NearestOpposite;
    internal static IBlockBehavior DiodeBlock => Diode;
    internal static IBlockBehavior DoorBlock { get; } = Placing(BlockPlacements.Door);
    internal static IBlockBehavior DoublePlantBlock { get; } = Placing(BlockPlacements.DoublePlant);
    internal static IBlockBehavior DriedGhastBlock => HorizontalOppositeWaterlogged;
    internal static IBlockBehavior EndPortalFrameBlock { get; } = Placing(BlockPlacements.EndPortalFrame);
    internal static IBlockBehavior EndRodBlock { get; } = Placing(BlockPlacements.EndRod);
    internal static IBlockBehavior EnderChestBlock => HorizontalOppositeWaterlogged;
    internal static IBlockBehavior FaceAttachedHorizontalDirectionalBlock { get; } =
        Placing(BlockPlacements.FaceAttached);
    internal static IBlockBehavior FarmlandBlock { get; } = Placing(BlockPlacements.Farmland);
    internal static IBlockBehavior FenceBlock { get; } = new FenceBlockBehavior();
    internal static IBlockBehavior FenceGateBlock { get; } = Placing(BlockPlacements.FenceGate);
    internal static IBlockBehavior FlowerBedBlock => Segmented;
    internal static IBlockBehavior GlazedTerracottaBlock => HorizontalOpposite;
    internal static IBlockBehavior GrowingPlantBlock { get; } = Placing(BlockPlacements.GrowingPlant);
    internal static IBlockBehavior HangingRootsBlock => Waterlogged;
    internal static IBlockBehavior HeavyCoreBlock => Waterlogged;
    internal static IBlockBehavior HopperBlock { get; } = Placing(BlockPlacements.Hopper);
    internal static IBlockBehavior HugeMushroomBlock { get; } = Placing(BlockPlacements.HugeMushroom);
    internal static IBlockBehavior InfestedRotatedPillarBlock => Pillar;
    internal static IBlockBehavior IronBarsBlock { get; } = new IronBarsPlacementBehavior();
    internal static IBlockBehavior JigsawBlock { get; } = Placing(BlockPlacements.Jigsaw);
    internal static IBlockBehavior KelpBlock => Kelp;
    internal static IBlockBehavior LadderBlock { get; } = Placing(BlockPlacements.Ladder);
    internal static IBlockBehavior LanternBlock { get; } = Placing(BlockPlacements.Lantern);
    internal static IBlockBehavior LeafLitterBlock => Segmented;
    internal static IBlockBehavior LeavesBlock => Leaves;
    internal static IBlockBehavior LecternBlock => HorizontalOpposite;
    internal static IBlockBehavior LightningRodBlock { get; } = Placing(BlockPlacements.LightningRod);
    internal static IBlockBehavior LoomBlock => HorizontalOpposite;
    internal static IBlockBehavior MangrovePropaguleBlock { get; } = Placing(BlockPlacements.MangrovePropagule);
    internal static IBlockBehavior MangroveRootsBlock => Waterlogged;
    internal static IBlockBehavior MossyCarpetBlock { get; } = Placing(BlockPlacements.MossyCarpet);
    internal static IBlockBehavior MultifaceBlock { get; } = Placing(BlockPlacements.Multiface);
    internal static IBlockBehavior NoteBlock { get; } = new NoteBlockBehavior();
    internal static IBlockBehavior ObserverBlock => Observer;
    internal static IBlockBehavior PathBlock { get; } = Placing(BlockPlacements.Path);
    internal static IBlockBehavior PistonBaseBlock => Piston;
    internal static IBlockBehavior PitcherCropBlock { get; } = Placing(context => context.Block);
    internal static IBlockBehavior PotentSulfurBlock { get; } = new PotentSulfurBlockBehavior();
    internal static IBlockBehavior RedstoneLampBlock => Lamp;
    internal static IBlockBehavior RedstoneWallTorchBlock { get; } = Placing(BlockPlacements.RedstoneWallTorch);
    internal static IBlockBehavior RedstoneWireBlock { get; } = Placing(BlockPlacements.RedstoneWire);
    internal static IBlockBehavior RepeaterBlock => Repeater;
    internal static IBlockBehavior RotatedPillarBlock => Pillar;
    internal static IBlockBehavior ScaffoldingBlock { get; } = Placing(BlockPlacements.Scaffolding);
    internal static IBlockBehavior SculkSensorBlock => Waterlogged;
    internal static IBlockBehavior SculkShriekerBlock => Waterlogged;
    internal static IBlockBehavior SeaPickleBlock { get; } = Placing(BlockPlacements.SeaPickle);
    internal static IBlockBehavior SeagrassBlock => Seagrass;
    internal static IBlockBehavior ShelfBlock { get; } = Placing(BlockPlacements.Shelf);
    internal static IBlockBehavior ShelfMushroomBlock => OnWall;
    internal static IBlockBehavior ShulkerBoxBlock => ClickedFace;
    internal static IBlockBehavior SlabBlock { get; } = Placing(BlockPlacements.Slab);
    internal static IBlockBehavior SmallDripleafBlock { get; } = Placing(BlockPlacements.SmallDripleaf);
    internal static IBlockBehavior SnowLayerBlock { get; } = Placing(BlockPlacements.SnowLayer);
    internal static IBlockBehavior SnowyBlock { get; } = Placing(BlockPlacements.Snowy);
    internal static IBlockBehavior SpeleothemBlock { get; } = new SulfurSpikeBehavior();
    internal static IBlockBehavior StairBlock { get; } = Placing(BlockPlacements.Stairs);
    internal static IBlockBehavior StandingSignBlock { get; } = Placing(BlockPlacements.StandingSign);
    internal static IBlockBehavior StonecutterBlock => HorizontalOpposite;
    internal static IBlockBehavior TallSeagrassBlock { get; } = Placing(BlockPlacements.TallSeagrass);
    internal static IBlockBehavior TestBlock { get; } = Placing(context => context.Block);
    internal static IBlockBehavior TrapDoorBlock { get; } = Placing(BlockPlacements.Trapdoor);
    internal static IBlockBehavior TripWireBlock { get; } = Placing(BlockPlacements.TripWire);
    internal static IBlockBehavior TripWireHookBlock { get; } = Placing(BlockPlacements.TripWireHook);
    internal static IBlockBehavior TurtleEggBlock { get; } = Placing(BlockPlacements.TurtleEgg);
    internal static IBlockBehavior VaultBlock => HorizontalOpposite;
    internal static IBlockBehavior VineBlock { get; } = Placing(BlockPlacements.Vine);
    internal static IBlockBehavior WallBannerBlock => OnWall;
    internal static IBlockBehavior WallBlock { get; } = Placing(BlockPlacements.Wall);
    internal static IBlockBehavior WallHangingSignBlock { get; } = Placing(BlockPlacements.WallHangingSign);
    internal static IBlockBehavior WallSignBlock { get; } = Placing(BlockPlacements.WallSign);
    internal static IBlockBehavior WallSkullBlock { get; } = Placing(BlockPlacements.WallSkull);
    internal static IBlockBehavior WallTorchBlock => OnWall;
    internal static IBlockBehavior WaterloggedTransparentBlock => Waterlogged;

    // Vanilla skulls follow the player's rotation and take power, including subclasses such as player and
    // wither-skeleton heads.
    internal static IBlockBehavior SkullBlock { get; } = Placing(BlockPlacements.StandingSkull);

    /// <returns>The implemented behaviour of the vanilla class that owns this block's placement override, or null.</returns>
    public static IBlockBehavior? For(IBlock block) => BlocksRegistry.GetPlacementBehavior(block.RegistryId);

    /// <summary>
    /// Vanilla <c>getStateForPlacement</c> for a vanilla block: its class's placement, or its default state when its
    /// class doesn't override it.
    /// </summary>
    /// <returns>The state to place, or null where vanilla refuses the placement.</returns>
    public static IBlock? GetState(BlockPlaceContext context) =>
        For(context.Block) is IBlockBehavior behavior ? behavior.GetStateForPlacement(context) : context.Block;

    private static DerivedPlacement Placing(Func<BlockPlaceContext, IBlock?> place) => new(place);

    /// <summary>A placement that is a pure function of the context.</summary>
    private sealed class DerivedPlacement(Func<BlockPlaceContext, IBlock?> derive) : IBlockBehavior
    {
        public IBlock? GetStateForPlacement(BlockPlaceContext context) => derive(context);
    }

    /// <summary>
    /// Vanilla <c>BambooStalkBlock.getStateForPlacement</c>: a stalk on bamboo or a sapling, a sapling on other
    /// plantable ground unless bamboo stands two below, and nothing in fluid or on unplantable ground.
    /// </summary>
    private sealed class BambooStalkPlacement : IBlockBehavior
    {
        public IBlock? GetStateForPlacement(BlockPlaceContext context)
        {
            var position = context.Position;
            if (context.Level.GetBlock(position).GetFluid() != FluidKind.Empty)
                return null;

            if (!BlockSurvival.BambooMayStay(new BlockReader(context.Level.AsShapeLevel()), position))
                return null;

            var below = context.Level.GetBlock(position + Vector.Down);
            if (below.Is(BlockIds.BambooSapling))
                return context.Block.WithProperty("leaves", "none");

            if (below.Is(BlockIds.Bamboo))
                return context.Block.WithProperty("leaves", below.GetProperty("leaves") == "none" ? "none" : "small");

            var belowBelow = context.Level.GetBlock(position + Vector.Down * 2);
            return belowBelow.Is(BlockIds.Bamboo)
                ? context.Block.WithProperty("leaves", belowBelow.GetProperty("leaves"))
                : BlocksRegistry.GetDefault(BlockIds.BambooSapling);
        }
    }

    private sealed class KelpPlacement : IBlockBehavior
    {
        public IBlock? GetStateForPlacement(BlockPlaceContext context)
        {
            var position = context.Position;
            var block = context.Level.GetBlock(position);
            var fluid = block.GetFluid();

            if ((fluid != FluidKind.Water && fluid != FluidKind.FlowingWater)
                || block.FluidAmount() < 8)
                    return null;

            if (context.Level.GetBlock(position + Vector.Up).RegistryId is BlockIds.Kelp or BlockIds.KelpPlant)
                return BlocksRegistry.KelpPlant;
            else
                return BlocksRegistry.Kelp.WithProperty("age", context.Level.Random.NextInt(25));
        }
    }

    private sealed class SeagrassPlacement : IBlockBehavior
    {
        public IBlock? GetStateForPlacement(BlockPlaceContext context)
        {
            var position = context.Position;
            var block = context.Level.GetBlock(position);
            var fluid = block.GetFluid();

            if ((fluid != FluidKind.Water && fluid != FluidKind.FlowingWater)
                || block.FluidAmount() < 8)
                return null;

            return context.Block;
        }
    }

    private sealed class HorizontalFacing(bool opposite) : IBlockBehavior
    {
        public IBlock? GetStateForPlacement(BlockPlaceContext context)
        {
            var direction = opposite ? context.HorizontalDirection.Opposite() : context.HorizontalDirection;
            return context.Block.WithProperty("facing", FeatureHelpers.FaceName(direction));
        }
    }

    private sealed class NearestFacing : IBlockBehavior
    {
        public IBlock? GetStateForPlacement(BlockPlaceContext context) =>
            context.Block.WithProperty("facing", FeatureHelpers.FaceName(context.NearestLookDirection.Opposite()));
    }

    private sealed class ClickedFaceFacing(bool opposite) : IBlockBehavior
    {
        public IBlock? GetStateForPlacement(BlockPlaceContext context)
        {
            var face = context.ClickedFace;
            if (opposite)
                face = face is BlockFace.Up or BlockFace.Down ? BlockFace.Down : face.Opposite();

            return context.Block.WithProperty("facing", FeatureHelpers.FaceName(face));
        }
    }
}
