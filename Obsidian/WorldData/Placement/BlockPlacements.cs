using Obsidian.API.Blocks;
using Obsidian.WorldData.BlockNotifications;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Features.Tree;
using Obsidian.WorldData.Generators;
using static Obsidian.WorldData.Placement.PlaceContexts;

namespace Obsidian.WorldData.Placement;

/// <summary>
/// The states vanilla blocks take when a player places them: one method per vanilla class that overrides
/// <c>getStateForPlacement</c>, bound to its blocks by <see cref="PlacementBehaviors"/>. A method returns null where
/// vanilla refuses the placement.
/// </summary>
/// <remarks>
/// This file holds blocks that face, turn, pair up or take a shape from their neighbors;
/// <c>BlockPlacements.Attached.cs</c> holds blocks that attach to a face, and <c>BlockPlacements.Plants.cs</c> plants and
/// blocks that stack in one space.
/// </remarks>
internal static partial class BlockPlacements
{
    private static readonly WorldgenTags.BlockGroup walls = WorldgenTags.Blocks("walls");

    /// <summary>Vanilla <c>RotatedPillarBlock</c>: along the axis of the clicked face.</summary>
    public static IBlock Pillar(BlockPlaceContext context) =>
        context.Block.WithProperty("axis", AxisName(context.ClickedFace));

    /// <summary>Vanilla <c>ChainBlock</c>: a waterloggable pillar.</summary>
    public static IBlock Chain(BlockPlaceContext context) => Pillar(context).WaterloggedFor(context);

    /// <summary>Blocks that face away from the player, like furnaces and carved pumpkins.</summary>
    public static IBlock FacingAway(BlockPlaceContext context) =>
        context.Block.WithProperty("facing", Name(context.HorizontalDirection.Opposite()));

    /// <summary>Blocks that face away from the player and take water, like copper golem statues.</summary>
    public static IBlock FacingAwayWaterlogged(BlockPlaceContext context) =>
        FacingAway(context).WaterloggedFor(context);

    /// <summary>Blocks that only take the water they replace, like barriers and sculk sensors.</summary>
    public static IBlock Waterlogged(BlockPlaceContext context) => context.Block.WaterloggedFor(context);

    /// <summary>Blocks that take water only where it's full, like conduits and coral.</summary>
    public static IBlock FullWaterlogged(BlockPlaceContext context) =>
        context.Block.WithProperty("waterlogged", context.ReplacesFullWater());

    /// <summary>Vanilla <c>AnvilBlock</c>: turned a quarter clockwise from the player.</summary>
    public static IBlock Anvil(BlockPlaceContext context) =>
        context.Block.WithProperty("facing", Name(context.HorizontalDirection.ClockWise()));

    /// <summary>Vanilla <c>EndPortalFrameBlock</c>: facing away from the player, without an eye.</summary>
    public static IBlock EndPortalFrame(BlockPlaceContext context) =>
        FacingAway(context).WithProperty("eye", false);

    /// <summary>Vanilla <c>CalibratedSculkSensorBlock</c>: a sculk sensor facing the way the player does.</summary>
    public static IBlock CalibratedSculkSensor(BlockPlaceContext context) =>
        Waterlogged(context).WithProperty("facing", Name(context.HorizontalDirection));

    /// <summary>Vanilla <c>ShelfBlock</c>: facing away from the player, powered by a signal there.</summary>
    public static IBlock Shelf(BlockPlaceContext context) =>
        FacingAwayWaterlogged(context).WithProperty("powered", context.Level.HasNeighborSignal(context.Position));

    /// <summary>Vanilla <c>CampfireBlock</c>: unlit in water, and a signal fire over hay.</summary>
    public static IBlock Campfire(BlockPlaceContext context)
    {
        var water = context.ReplacesWaterSource();
        var overHay = context.Level.GetBlock(context.Position + Vector.Down).Is(BlockIds.HayBlock);
        return context.Block
            .WithProperty("waterlogged", water)
            .WithProperty("signal_fire", overHay)
            .WithProperty("lit", !water)
            .WithProperty("facing", Name(context.HorizontalDirection));
    }

    /// <summary>Vanilla <c>DecoratedPotBlock</c>: facing the way the player does, whole.</summary>
    public static IBlock DecoratedPot(BlockPlaceContext context) => context.Block
        .WithProperty("facing", Name(context.HorizontalDirection))
        .WaterloggedFor(context)
        .WithProperty("cracked", false);

    /// <summary>Vanilla <c>HopperBlock</c>: pointing into the clicked block, or down when clicked on its top or bottom.</summary>
    public static IBlock Hopper(BlockPlaceContext context)
    {
        var into = context.ClickedFace.Opposite();
        var facing = into.IsVertical() ? BlockFace.Down : into;
        return context.Block.WithProperty("facing", Name(facing)).WithProperty("enabled", true);
    }

    /// <summary>Vanilla <c>EndRodBlock</c>: out of the clicked face, or back to back with an end rod it's put on.</summary>
    public static IBlock EndRod(BlockPlaceContext context)
    {
        var face = context.ClickedFace;
        var against = context.Level.GetBlock(context.Position.Offset(face.Opposite()));
        var backToBack = against.RegistryId == context.Block.RegistryId && against.GetProperty("facing") == Name(face);
        return context.Block.WithProperty("facing", Name(backToBack ? face.Opposite() : face));
    }

    /// <summary>Vanilla <c>LightningRodBlock</c>: out of the clicked face.</summary>
    public static IBlock LightningRod(BlockPlaceContext context) =>
        context.Block.WithProperty("facing", Name(context.ClickedFace)).WaterloggedFor(context);

    /// <summary>Vanilla <c>JigsawBlock</c>: front out of the clicked face, top up or away from the player.</summary>
    public static IBlock Jigsaw(BlockPlaceContext context)
    {
        var front = context.ClickedFace;
        var top = front.IsVertical() ? context.HorizontalDirection.Opposite() : BlockFace.Up;
        return context.Block.WithProperty("orientation", $"{Name(front)}_{Name(top)}");
    }

    /// <summary>Vanilla <c>HugeMushroomBlock</c>: each side shows cap or stem unless the same block is there.</summary>
    public static IBlock HugeMushroom(BlockPlaceContext context)
    {
        var state = context.Block;
        foreach (var side in Enum.GetValues<BlockFace>())
        {
            var neighbor = context.Level.GetBlock(context.Position.Offset(side));
            state = state.WithProperty(Name(side), neighbor.RegistryId != context.Block.RegistryId);
        }

        return state;
    }

    /// <summary>Vanilla <c>SnowyBlock</c>: grass and its kin are snowy under snow.</summary>
    public static IBlock Snowy(BlockPlaceContext context)
    {
        var above = context.Level.GetBlock(context.Position + Vector.Up);
        return context.Block.WithProperty("snowy", TreeBlocks.Snow.Contains(above));
    }

    /// <summary>
    /// Vanilla <c>FarmlandBlock</c> and <c>PathBlock</c>: dirt where a solid block above wouldn't let it stay.
    /// </summary>
    public static IBlock Farmland(BlockPlaceContext context)
    {
        var above = context.Level.GetBlock(context.Position + Vector.Up);
        var holds = !above.IsSolid() || above.BlockClass() == "FenceGateBlock" || above.Is(BlockIds.MovingPiston);
        return holds ? context.Block : BlocksRegistry.Dirt;
    }

    /// <summary>Vanilla <c>PathBlock</c>: dirt under a solid block other than a fence gate.</summary>
    public static IBlock Path(BlockPlaceContext context)
    {
        var above = context.Level.GetBlock(context.Position + Vector.Up);
        var holds = !above.IsSolid() || above.BlockClass() == "FenceGateBlock";
        return holds ? context.Block : BlocksRegistry.Dirt;
    }

    /// <summary>
    /// Vanilla <c>ConcretePowderBlock</c>: concrete straight away where it touches water.
    /// </summary>
    public static IBlock ConcretePowder(BlockPlaceContext context)
    {
        var solidifies = IsWater(context.Replaced()) || TouchesWater(context.Level, context.Position);
        if (!solidifies)
            return context.Block;

        var concrete = context.Block.UnlocalizedName.Replace("_concrete_powder", "_concrete");
        return BlockStateProperties.GetState(concrete);
    }

    // Vanilla ConcretePowderBlock.touchesLiquid, which only looks below when the powder replaces water itself, and
    // asks a neighbor for a sturdy face the way vanilla does.
    private static bool TouchesWater(IBlockLevel level, Vector position)
    {
        var tested = position;
        BlockFace[] sides = [BlockFace.Down, BlockFace.Up, BlockFace.North, BlockFace.South, BlockFace.West, BlockFace.East];
        foreach (var side in sides)
        {
            if (side == BlockFace.Down && !IsWater(level.GetBlock(tested)))
                continue;

            tested = position.Offset(side);
            var neighbor = level.GetBlock(tested);
            if (IsWater(neighbor) && !neighbor.IsFaceSturdy(side.Opposite()))
                return true;
        }

        return false;
    }

    private static bool IsWater(IBlock block) => block.GetFluid() is FluidKind.Water or FluidKind.FlowingWater;

    /// <summary>Vanilla <c>StairBlock</c>: facing the player, upside down from a high click, cornered by neighbors.</summary>
    public static IBlock Stairs(BlockPlaceContext context)
    {
        var face = context.ClickedFace;
        var top = face == BlockFace.Down || face != BlockFace.Up && context.ClickHeight() > 0.5;
        var state = context.Block
            .WithProperty("facing", Name(context.HorizontalDirection))
            .WithProperty("half", top ? "top" : "bottom")
            .WaterloggedFor(context);

        // The shape only depends on the stairs around, so one update from any side derives it.
        return ShapeUpdater.UpdateShape(
            View(context),
            state,
            context.Position,
            BlockFace.North,
            context.Level.GetBlock(context.Position.Offset(BlockFace.North)),
            scheduleFluidTick: false);
    }

    /// <summary>
    /// Vanilla <c>SlabBlock</c>: a slab put into a half slab of its kind makes it double; otherwise the half follows
    /// the click.
    /// </summary>
    public static IBlock Slab(BlockPlaceContext context)
    {
        var replaced = context.Replaced();
        if (replaced.RegistryId == context.Block.RegistryId)
            return replaced.WithProperty("type", "double").WithProperty("waterlogged", false);

        var face = context.ClickedFace;
        var top = face == BlockFace.Down || face != BlockFace.Up && context.ClickHeight() > 0.5;
        return context.Block.WithProperty("type", top ? "top" : "bottom").WaterloggedFor(context);
    }

    /// <summary>Vanilla <c>WallBlock</c>: connected to its neighbors, with a post where the block above needs one.</summary>
    public static IBlock Wall(BlockPlaceContext context)
    {
        BlockFace[] sides = [BlockFace.North, BlockFace.East, BlockFace.South, BlockFace.West];
        return ShapedBySides(context, context.Block.WaterloggedFor(context), sides);
    }

    /// <summary>
    /// Vanilla <c>TrapDoorBlock</c>: against the clicked side, high or low as clicked, or flat, facing away from the
    /// player, on a top or bottom face. A signal there opens it.
    /// </summary>
    public static IBlock Trapdoor(BlockPlaceContext context)
    {
        var face = context.ClickedFace;
        var state = context.Block;
        if (!context.ReplacesClicked && !face.IsVertical())
        {
            state = state.WithProperty("facing", Name(face))
                .WithProperty("half", context.ClickHeight() > 0.5 ? "top" : "bottom");
        }
        else
        {
            state = state.WithProperty("facing", Name(context.HorizontalDirection.Opposite()))
                .WithProperty("half", face == BlockFace.Up ? "bottom" : "top");
        }

        if (context.Level.HasNeighborSignal(context.Position))
            state = state.WithProperty("open", true).WithProperty("powered", true);

        return state.WaterloggedFor(context);
    }

    /// <summary>
    /// Vanilla <c>FenceGateBlock</c>: facing the way the player does, lowered between walls, and open where powered.
    /// </summary>
    public static IBlock FenceGate(BlockPlaceContext context)
    {
        var facing = context.HorizontalDirection;
        var side = facing.ClockWise();
        var level = context.Level;
        var inWall = walls.Contains(level.GetBlock(context.Position.Offset(side)))
            || walls.Contains(level.GetBlock(context.Position.Offset(side.Opposite())));
        var open = level.HasNeighborSignal(context.Position);
        return context.Block
            .WithProperty("facing", Name(facing))
            .WithProperty("open", open)
            .WithProperty("powered", open)
            .WithProperty("in_wall", inWall);
    }

    /// <summary>
    /// Vanilla <c>DoorBlock</c>: the lower half, facing the way the player does, with room above for the upper half.
    /// The hinge goes by the walls and doors beside it, or the side of the door the player clicked.
    /// </summary>
    public static IBlock? Door(BlockPlaceContext context)
    {
        var position = context.Position;
        var level = context.Level;
        var belowTop = level.Level is AbstractLevel live && !live.IsOutsideBuildHeight(position.Y + 1);
        if (!belowTop || !level.GetBlock(position + Vector.Up).CanBeReplaced())
            return null;

        var facing = context.HorizontalDirection;
        var powered = level.HasNeighborSignal(position) || level.HasNeighborSignal(position + Vector.Up);
        return context.Block
            .WithProperty("facing", Name(facing))
            .WithProperty("hinge", Hinge(context, facing))
            .WithProperty("powered", powered)
            .WithProperty("open", powered)
            .WithProperty("half", "lower");
    }

    // Vanilla DoorBlock.getHinge: against the wall beside it, beside a door so the two open as a pair, or where the
    // player clicked.
    private static string Hinge(BlockPlaceContext context, BlockFace facing)
    {
        var level = context.Level;
        var left = context.Position.Offset(facing.CounterClockWise());
        var right = context.Position.Offset(facing.ClockWise());
        var leftState = level.GetBlock(left);
        var rightState = level.GetBlock(right);
        var leftWalls = Full(leftState) + Full(level.GetBlock(left + Vector.Up));
        var rightWalls = Full(rightState) + Full(level.GetBlock(right + Vector.Up));
        var walls = rightWalls - leftWalls;
        var doorLeft = IsLowerDoor(leftState);
        var doorRight = IsLowerDoor(rightState);

        if (doorLeft && !doorRight || walls > 0)
            return "right";

        if (doorRight && !doorLeft || walls < 0)
            return "left";

        // The click, across the door's width.
        var step = facing.ToVector();
        var clickX = context.ClickedPosition.X + context.ClickedAt.X - context.Position.X;
        var clickZ = context.ClickedPosition.Z + context.ClickedAt.Z - context.Position.Z;
        var clickedRight = step.X < 0 && clickZ < 0.5 || step.X > 0 && clickZ > 0.5
            || step.Z < 0 && clickX > 0.5 || step.Z > 0 && clickX < 0.5;
        return clickedRight ? "right" : "left";

        static int Full(IBlock block) => block.IsCollisionShapeFullBlock() ? 1 : 0;
    }

    // The lower half of any door, as vanilla's hinge pairs a door with a door of another kind too.
    private static bool IsLowerDoor(IBlock neighbor) =>
        neighbor.BlockClass() is "DoorBlock" or "WeatheringCopperDoorBlock" && neighbor.GetProperty("half") == "lower";

    /// <summary>
    /// Vanilla <c>BedBlock</c>: the foot, facing the way the player does, with room ahead for the head.
    /// </summary>
    public static IBlock? Bed(BlockPlaceContext context)
    {
        var facing = context.HorizontalDirection;
        if (!context.Level.GetBlock(context.Position.Offset(facing)).CanBeReplaced())
            return null;

        return context.Block.WithProperty("facing", Name(facing));
    }

    /// <summary>
    /// Vanilla <c>ChestBlock</c>: facing away from the player, pairing with a single chest of its kind beside it that
    /// faces the same way. Sneaking pairs it with the chest clicked instead, and otherwise leaves it single.
    /// </summary>
    public static IBlock Chest(BlockPlaceContext context)
    {
        var facing = context.HorizontalDirection.Opposite();
        var type = "single";
        var face = context.ClickedFace;
        var sneaking = context.Sneaking();
        if (!face.IsVertical() && sneaking
            && PartnerFacing(context, face.Opposite()) is BlockFace partner
            && !SameAxis(partner, face))
        {
            facing = partner;
            type = partner.CounterClockWise() == face.Opposite() ? "right" : "left";
        }

        if (type == "single" && !sneaking)
        {
            if (PartnerFacing(context, facing.ClockWise()) == facing)
                type = "left";
            else if (PartnerFacing(context, facing.CounterClockWise()) == facing)
                type = "right";
        }

        return context.Block
            .WithProperty("facing", Name(facing))
            .WithProperty("type", type)
            .WaterloggedFor(context);
    }

    /// <summary>
    /// Vanilla <c>CopperChestBlock</c>: a chest that, paired with another copper chest, takes the less weathered of the
    /// two.
    /// </summary>
    public static IBlock CopperChest(BlockPlaceContext context)
    {
        var state = Chest(context);
        var connected = ConnectedDirection(state);
        if (connected is not BlockFace direction)
            return state;

        var partner = context.Level.GetBlock(context.Position.Offset(direction));
        if (CopperAge(partner.UnlocalizedName) is not int partnerAge)
            return state;

        var ownAge = CopperAge(state.UnlocalizedName)!.Value;
        var waxed = state.UnlocalizedName.Contains("waxed_");
        var partnerWaxed = partner.UnlocalizedName.Contains("waxed_");
        var name = ownAge <= partnerAge ? state.UnlocalizedName : partner.UnlocalizedName;

        // A waxed chest paired with an unwaxed one comes out unwaxed.
        if (waxed != partnerWaxed)
            name = name.Replace("waxed_", "");

        var chosen = BlockStateProperties.GetState(name);
        return chosen.WithProperty("facing", state.GetProperty("facing")!)
            .WithProperty("type", state.GetProperty("type")!)
            .WithProperty("waterlogged", state.GetProperty("waterlogged")!);
    }

    // How weathered a copper chest is, from 0 (fresh) to 3 (oxidized), or null for another block.
    private static int? CopperAge(string name)
    {
        if (!name.EndsWith("copper_chest", StringComparison.Ordinal))
            return null;

        if (name.Contains("oxidized"))
            return 3;

        if (name.Contains("weathered"))
            return 2;

        return name.Contains("exposed") ? 1 : 0;
    }

    /// <summary>
    /// Vanilla <c>ChestBlock.chestCanConnectTo</c>: a chest pairs with a chest of its own kind, and a copper chest with
    /// any copper chest (the <c>copper_chests</c> tag), however weathered or waxed.
    /// </summary>
    public static bool ChestsConnect(IBlock chest, IBlock other)
    {
        if (!IsCopperChest(chest))
            return other.RegistryId == chest.RegistryId;

        return TagsRegistry.Block.CopperChests.Entries.Contains(other.RegistryId)
            && other.GetProperty("type") is not null;
    }

    /// <summary>Whether a block is a copper chest of any weathering, waxed or not.</summary>
    public static bool IsCopperChest(IBlock block) =>
        block.BlockClass() is "CopperChestBlock" or "WeatheringCopperChestBlock";

    // The facing of a single chest this one can pair with in that direction, if there is one.
    private static BlockFace? PartnerFacing(BlockPlaceContext context, BlockFace direction)
    {
        var other = context.Level.GetBlock(context.Position.Offset(direction));
        if (!ChestsConnect(context.Block, other) || other.GetProperty("type") != "single")
            return null;

        return FeatureHelpers.ParseFace(other.GetProperty("facing"));
    }

    /// <summary>Vanilla <c>ChestBlock.getConnectedDirection</c>: where the other half of a double chest is.</summary>
    public static BlockFace? ConnectedDirection(IBlock chest)
    {
        var facing = FeatureHelpers.ParseFace(chest.GetProperty("facing"));
        return chest.GetProperty("type") switch
        {
            "left" => facing.ClockWise(),
            "right" => facing.CounterClockWise(),
            _ => null
        };
    }

    /// <summary>
    /// Vanilla <c>BaseRailBlock</c>: straight along the way the player faces. Rails curve and climb toward other rails
    /// once placed.
    /// </summary>
    public static IBlock Rail(BlockPlaceContext context)
    {
        var facing = context.HorizontalDirection;
        var eastWest = facing is BlockFace.East or BlockFace.West;
        return context.Block
            .WithProperty("shape", eastWest ? "east_west" : "north_south")
            .WaterloggedFor(context);
    }

    /// <summary>Vanilla <c>RedstoneWireBlock</c>: a cross, connected to whatever it can reach.</summary>
    public static IBlock RedstoneWire(BlockPlaceContext context)
    {
        var view = new Redstone.WireView(context.Level.GetBlock, block => block.IsSignalSource());
        return Redstone.WireConnections.Connect(view, Redstone.WireConnections.Cross, context.Position);
    }

    /// <summary>Vanilla <c>TripWireBlock</c>: connected to hooks facing it and to string beside it.</summary>
    public static IBlock TripWire(BlockPlaceContext context)
    {
        var state = context.Block;
        foreach (var side in (BlockFace[])[BlockFace.North, BlockFace.East, BlockFace.South, BlockFace.West])
        {
            var neighbor = context.Level.GetBlock(context.Position.Offset(side));
            var hook = neighbor.Is(BlockIds.TripwireHook) && neighbor.GetProperty("facing") == Name(side.Opposite());
            state = state.WithProperty(Name(side), hook || neighbor.Is(BlockIds.Tripwire));
        }

        return state;
    }

    /// <summary>Vanilla <c>ChorusPlantBlock</c>: connected to chorus beside it, and down to what holds it.</summary>
    public static IBlock ChorusPlant(BlockPlaceContext context)
    {
        var supports = WorldgenTags.Blocks("supports_chorus_plant");
        var state = context.Block;
        foreach (var side in Enum.GetValues<BlockFace>())
        {
            var neighbor = context.Level.GetBlock(context.Position.Offset(side));
            var connects = neighbor.RegistryId == context.Block.RegistryId || neighbor.Is(BlockIds.ChorusFlower)
                || side == BlockFace.Down && supports.Contains(neighbor);
            state = state.WithProperty(Name(side), connects);
        }

        return state;
    }

    /// <summary>
    /// Vanilla <c>ScaffoldingBlock</c>: as far from support as the scaffolding it stands on or beside, at the bottom when
    /// it hangs free.
    /// </summary>
    public static IBlock Scaffolding(BlockPlaceContext context)
    {
        var distance = ScaffoldingDistance(context.Level, context.Position);
        var below = context.Level.GetBlock(context.Position + Vector.Down);
        var bottom = distance > 0 && below.RegistryId != context.Block.RegistryId;
        return context.Block
            .WaterloggedFor(context)
            .WithProperty("distance", distance)
            .WithProperty("bottom", bottom);
    }

    /// <summary>
    /// Vanilla <c>ScaffoldingBlock.getDistance</c>: 0 on a sturdy block, otherwise one more than the nearest
    /// scaffolding beside it, or what's below; 7 when nothing holds it.
    /// </summary>
    public static int ScaffoldingDistance(IBlockLevel level, Vector position)
    {
        var below = level.GetBlock(position + Vector.Down);
        var distance = 7;
        if (below.Is(BlockIds.Scaffolding))
            distance = int.Parse(below.GetProperty("distance")!);
        else if (below.IsFaceSturdy(BlockFace.Up))
            return 0;

        foreach (var side in (BlockFace[])[BlockFace.North, BlockFace.East, BlockFace.South, BlockFace.West])
        {
            var beside = level.GetBlock(position.Offset(side));
            if (!beside.Is(BlockIds.Scaffolding))
                continue;

            distance = Math.Min(distance, int.Parse(beside.GetProperty("distance")!) + 1);
            if (distance == 1)
                break;
        }

        return distance;
    }

    // The block updated against its neighbor on each of the sides, as its shape updates would after it's placed.
    private static IBlock ShapedBySides(BlockPlaceContext context, IBlock state, BlockFace[] sides)
    {
        var view = View(context);
        foreach (var side in sides)
        {
            var neighbor = context.Level.GetBlock(context.Position.Offset(side));
            state = ShapeUpdater.UpdateShape(view, state, context.Position, side, neighbor, scheduleFluidTick: false);
        }

        return state;
    }

    private static LiveShapeView View(BlockPlaceContext context) => new((IBlockChangeWorld)context.Level.Level);
}
