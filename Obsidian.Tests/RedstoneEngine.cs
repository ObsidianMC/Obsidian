using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Obsidian.API;
using Obsidian.Registries;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Placement;
using Obsidian.WorldData.Redstone;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// The directional power engine (R3, see docs/redstone/02-core-circuits.md): six faces, conductor boundaries, analog levels, wire
/// output, sculk sensor output, deterministic results and unloaded chunk edges.
/// </summary>
/// <remarks>
/// Sources are registered per test on the block class of a real block. Basic sources (levers, buttons, torches, the redstone block)
/// belong to R4, so R3 tests use a source behavior that gives exactly the signal each test states.
/// </remarks>
public class RedstoneEngineTests
{
    private const string Floor = "minecraft:stone";
    // Glass is deliberately used for isolated engine tests. RedstoneBlock is used where ShapeUpdater must see generated
    // IsSignalSource metadata, as a real wire placement does.
    private const string Source = "minecraft:glass";
    private const string ActualSignalSource = "minecraft:redstone_block";

    private static readonly BlockFace[] faces = [BlockFace.Down, BlockFace.Up, BlockFace.North, BlockFace.South, BlockFace.West, BlockFace.East];
    private static readonly BlockFace[] sides = [BlockFace.North, BlockFace.South, BlockFace.West, BlockFace.East];

    [Theory]
    [InlineData(BlockFace.Down)]
    [InlineData(BlockFace.Up)]
    [InlineData(BlockFace.North)]
    [InlineData(BlockFace.South)]
    [InlineData(BlockFace.West)]
    [InlineData(BlockFace.East)]
    public async Task SourceSignalReachesOnlyTheFaceItEmitsOn(BlockFace emit)
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 15, 62, 66, 0, 15);
        var source = new Vector(8, 64, 8);
        world.Redstone.Register(BlockClassOf(Source), new TestSource(signal: (direction, _) => direction == emit.Opposite() ? 15 : 0));
        await world.Place(source, BlocksRegistry.Get(Source));

        foreach (var face in faces)
        {
            // The receiver on `face` of the source sees the signal from the side facing it.
            var expected = face == emit;
            Assert.Equal(expected, world.Engine.HasSignal(source.Offset(face), face.Opposite()));
        }
    }

    [Theory]
    [InlineData("minecraft:stone", true)]
    [InlineData("minecraft:air", false)]
    public async Task DirectSignalCrossesOnlyAConductor(string middle, bool expected)
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 15, 62, 68, 0, 15);
        var source = new Vector(8, 63, 8);
        var middlePos = new Vector(8, 64, 8);
        var receiver = new Vector(8, 65, 8);
        // Direction is receiver -> source (vanilla getDirectSignalTo): the receiver lies above the source, so it asks with Down.
        world.Redstone.Register(BlockClassOf(Source), new TestSource(direct: (direction, _) => direction == BlockFace.Down ? 15 : 0));
        await world.Place(source, BlocksRegistry.Get(Source));
        await world.Place(middlePos, BlocksRegistry.Get(middle));

        Assert.Equal(middle == Floor, BlocksRegistry.Get(middle).IsRedstoneConductor());
        Assert.Equal(expected, world.Engine.HasSignal(receiver, BlockFace.Down));
    }

    [Fact]
    public async Task WeakSignalDoesNotCrossAConductor()
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 15, 62, 68, 0, 15);
        var source = new Vector(8, 63, 8);
        world.Redstone.Register(BlockClassOf(Source), new TestSource(signal: (_, _) => 15));
        await world.Place(source, BlocksRegistry.Get(Source));
        await world.Place(new Vector(8, 64, 8), BlocksRegistry.Get(Floor));

        Assert.False(world.Engine.HasSignal(new Vector(8, 65, 8), BlockFace.Down));
    }

    [Theory]
    [InlineData(15)]
    [InlineData(9)]
    [InlineData(1)]
    public async Task AnalogPowerDecaysOneStepPerWire(int strength)
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 15, 62, 66, 0, 15);
        var source = new Vector(2, 64, 8);
        world.Redstone.Register(BlockClassOf(Source), new TestSource(signal: (direction, _) => sides.Contains(direction) ? strength : 0));
        await world.Place(source, BlocksRegistry.Get(Source));
        for (var x = 3; x <= 12; x++)
        {
            await world.Place(new Vector(x, 63, 8), BlocksRegistry.Get(Floor));
            await PlaceWire(world, new Vector(x, 64, 8));
        }

        for (var k = 0; k < 10; k++)
            Assert.Equal(Math.Max(0, strength - k), Power(world, new Vector(3 + k, 64, 8)));
    }

    [Fact]
    public async Task FinalPowerDoesNotDependOnPlacementOrder()
    {
        var wires = Enumerable.Range(3, 10).Select(x => new Vector(x, 64, 8)).ToArray();
        var source = new Vector(2, 64, 8);

        var forward = new RedstoneTestWorld();
        await BuildLine(forward, source, wires, sourceFirst: true);
        var reverse = new RedstoneTestWorld();
        await BuildLine(reverse, source, wires.Reverse().ToArray(), sourceFirst: false);
        var repeat = new RedstoneTestWorld();
        await BuildLine(repeat, source, wires, sourceFirst: true);

        var expected = Dump(forward, wires);
        Assert.Equal(expected, Dump(reverse, wires));
        Assert.Equal(expected, Dump(repeat, wires));
        // The source gives 15 to the first wire, and each wire further along is one lower.
        for (var k = 0; k < wires.Length; k++)
            Assert.Equal(Math.Max(0, 15 - k), Power(forward, wires[k]));
    }

    [Fact]
    public async Task WireBesideUnloadedChunkReadsNoSignalAndLoadsNothing()
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 15, 62, 66, 0, 15);
        await world.Place(new Vector(15, 63, 8), BlocksRegistry.Get(Floor));
        await PlaceWire(world, new Vector(15, 64, 8));
        // The source sits at x = 16 in chunk 1, which isn't loaded: the wire must see nothing and nothing may load.
        Assert.Equal(0, world.Engine.GetSignal(new Vector(16, 64, 8), BlockFace.West));
        Assert.Null(world.GetLoadedBlock(new Vector(16, 64, 8)));
        Assert.Equal(0, Power(world, new Vector(15, 64, 8)));
    }

    [Fact]
    public async Task LoadedSourceAtTheSameEdgeDrivesTheWire()
    {
        // Control for the test above: with the source loaded, the same wire is powered.
        var world = new RedstoneTestWorld();
        world.Load(0, 15, 62, 66, 0, 15);
        world.Redstone.Register(BlockClassOf(Source), new TestSource(signal: (direction, _) => sides.Contains(direction) ? 15 : 0));
        await world.Place(new Vector(13, 64, 8), BlocksRegistry.Get(Source));
        await world.Place(new Vector(14, 63, 8), BlocksRegistry.Get(Floor));
        await PlaceWire(world, new Vector(14, 64, 8));

        Assert.Equal(15, Power(world, new Vector(14, 64, 8)));
    }

    [Fact]
    public async Task NewlyPlacedWireDerivesConnectionsFromAnExistingSignalSource()
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 15, 62, 66, 0, 15);
        var source = new Vector(7, 64, 8);
        var wire = new Vector(8, 64, 8);
        await world.Place(source, BlocksRegistry.Get(ActualSignalSource));
        await world.Place(new Vector(wire.X, 63, wire.Z), BlocksRegistry.Get(Floor));
        await PlaceWire(world, wire);

        // The source was already present. Placement itself must derive both the source-side connection and the line's open end.
        var state = world.GetLoadedBlock(wire)!;
        Assert.Equal("side", state.GetProperty("west"));
        Assert.Equal("side", state.GetProperty("east"));
        Assert.Equal(15, Power(world, wire));
        Assert.Equal(15, world.Engine.GetSignal(wire, BlockFace.West));
    }

    [Theory]
    [InlineData(BlockFace.Up, "floor", "south")]
    [InlineData(BlockFace.Down, "ceiling", "south")]
    [InlineData(BlockFace.North, "wall", "north")]
    public void MountedComponentPlacementDerivesFaceAndFacing(BlockFace clickedFace, string expectedFace, string expectedFacing)
    {
        var placed = BasicRedstoneComponents.DerivePlacementState(BlocksRegistry.Get("minecraft:lever"), clickedFace, 0);
        Assert.Equal(expectedFace, placed.GetProperty("face"));
        Assert.Equal(expectedFacing, placed.GetProperty("facing"));
    }

    [Fact]
    public async Task PoweredFloorLeverGivesWeakPowerEverywhereAndDirectPowerToItsSupport()
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 15, 62, 66, 0, 15);
        var lever = new Vector(8, 64, 8);
        await world.Place(lever, BlocksRegistry.Get("minecraft:lever").WithProperty("face", "floor").WithProperty("facing", "north").WithProperty("powered", true));

        Assert.Equal(15, world.Engine.GetSignal(lever, BlockFace.North));
        // Oracle-verified: the powered floor lever powers the support below it, not the block above.
        Assert.Equal(15, world.Engine.GetDirectSignal(lever, BlockFace.Up));
        Assert.Equal(0, world.Engine.GetDirectSignal(lever, BlockFace.Down));
        Assert.Equal(0, world.Engine.GetDirectSignal(lever, BlockFace.North));
    }

    [Fact]
    public async Task RedstoneTorchDoesNotPowerItsSupportAndDirectlyPowersAbove()
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 15, 62, 66, 0, 15);
        var torch = new Vector(8, 64, 8);
        await world.Place(torch, BlocksRegistry.Get("minecraft:redstone_torch"));

        Assert.Equal(0, world.Engine.GetSignal(torch, BlockFace.Up));
        Assert.Equal(15, world.Engine.GetSignal(torch, BlockFace.North));
        Assert.Equal(15, world.Engine.GetDirectSignal(torch, BlockFace.Down));
        Assert.Equal(0, world.Engine.GetDirectSignal(torch, BlockFace.Up));
    }

    [Fact]
    public async Task RedstoneBlockPowersAndUnpowersALamp()
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 15, 62, 66, 0, 15, ringChunks: 1);
        var source = new Vector(7, 64, 8);
        var lamp = new Vector(8, 64, 8);
        // Placed with setblock semantics: the lamp keeps its state until a neighbor changes.
        await world.Place(lamp, BlocksRegistry.Get("minecraft:redstone_lamp"));
        await world.Place(source, BlocksRegistry.Get("minecraft:redstone_block"));
        Assert.Equal("true", world.GetLoadedBlock(lamp)!.GetProperty("lit"));

        // Vanilla: an unpowered lamp goes out four ticks later, not at once.
        await world.Place(source, BlocksRegistry.Air);
        for (var tick = 0; tick < 3; tick++)
            await world.Advance();
        Assert.Equal("true", world.GetLoadedBlock(lamp)!.GetProperty("lit"));
        await world.Advance();
        Assert.Equal("false", world.GetLoadedBlock(lamp)!.GetProperty("lit"));
    }

    [Fact]
    public async Task BreakingATorchKeepsNearbyWires()
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 15, 62, 66, 0, 15);
        var stone = BlocksRegistry.Get("minecraft:stone");
        for (var x = 2; x <= 12; x++)
            await world.Place(new Vector(x, 63, 8), stone);

        var wire = BlocksRegistry.Get(Material.RedstoneWire);
        var wires = new[] { new Vector(3, 64, 8), new Vector(4, 64, 8), new Vector(5, 64, 8) };
        foreach (var p in wires)
            await world.Place(p, wire);

        var torch = new Vector(10, 64, 8);
        await world.Place(torch, BlocksRegistry.Get("minecraft:redstone_torch"));
        await world.Place(torch, BlocksRegistry.Air);

        foreach (var p in wires)
            Assert.Equal("minecraft:redstone_wire", world.GetLoadedBlock(p)!.UnlocalizedName);
    }

    [Fact]
    public async Task RedstoneTorchOnASideFaceBecomesAWallTorchThatSurvives()
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 15, 62, 66, 0, 15);
        var support = new Vector(8, 64, 8);
        var torch = new Vector(9, 64, 8);
        await world.Place(support, BlocksRegistry.Get("minecraft:stone"));
        var placed = BasicRedstoneComponents.DerivePlacementState(BlocksRegistry.Get("minecraft:redstone_torch"), BlockFace.East, 0);
        Assert.Equal("east", placed.GetProperty("facing"));

        await world.Place(torch, placed);

        Assert.Equal("minecraft:redstone_wall_torch", world.GetLoadedBlock(torch)!.UnlocalizedName);
        // Oracle-verified: a wall torch does not power the wall it is mounted on.
        Assert.Equal(0, world.Engine.GetDirectSignal(torch, BlockFace.West));
    }

    [Fact]
    public async Task PressedStonePlatePowersItsSupportDirectlyAndSidesWeakly()
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 15, 62, 66, 0, 15);
        var plate = new Vector(8, 64, 8);
        await world.Place(plate, BlocksRegistry.Get("minecraft:stone_pressure_plate").WithProperty("powered", true));

        Assert.Equal(15, world.Engine.GetDirectSignal(plate, BlockFace.Up));
        Assert.Equal(0, world.Engine.GetDirectSignal(plate, BlockFace.Down));
        Assert.Equal(15, world.Engine.GetSignal(plate, BlockFace.North));
    }

    [Fact]
    public async Task PressingAPlateSchedulesItsReleaseAndAStillOccupiedPlateRearms()
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 15, 62, 66, 0, 15);
        var plate = new Vector(8, 64, 8);
        await world.Place(plate, BlocksRegistry.Get("minecraft:stone_pressure_plate"));
        var plates = new PressurePlates(world);

        await plates.ApplyAsync(new Dictionary<Vector, int> { [plate] = 1 });
        Assert.Equal("true", world.GetLoadedBlock(plate)!.GetProperty("powered"));
        Assert.Contains(world.ScheduledPlateTicks, tick => tick.Position == plate && tick.Delay == 20);

        // Release fires while the entity is still standing on it: the plate stays pressed and re-arms.
        world.ScheduledPlateTicks.Clear();
        await plates.ApplyAsync(new Dictionary<Vector, int> { [plate] = 1 });
        await plates.ReleaseAsync(plate);
        Assert.Equal("true", world.GetLoadedBlock(plate)!.GetProperty("powered"));
        Assert.Contains(world.ScheduledPlateTicks, tick => tick.Position == plate && tick.Delay == 20);

        // Release with nothing on it releases the plate.
        await plates.ApplyAsync(new Dictionary<Vector, int>());
        await plates.ReleaseAsync(plate);
        Assert.Equal("false", world.GetLoadedBlock(plate)!.GetProperty("powered"));
    }

    [Fact]
    public async Task StonePlateIgnoresNonLivingEntitiesButWoodenPlateAcceptsThem()
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 15, 62, 66, 0, 15);
        var stone = new Vector(8, 64, 8);
        var oak = new Vector(10, 64, 8);
        await world.Place(stone, BlocksRegistry.Get("minecraft:stone_pressure_plate"));
        await world.Place(oak, BlocksRegistry.Get("minecraft:oak_pressure_plate"));
        var plates = new PressurePlates(world);

        // An item or projectile on both plates: only the wooden plate reads it (vanilla Sensitivity.MOBS vs EVERYTHING).
        var anyOnly = new PressurePlates.PlateOccupancy(new Dictionary<Vector, int> { [stone] = 1, [oak] = 1 }, new Dictionary<Vector, int>());
        await plates.ApplyAsync(anyOnly);
        Assert.Equal("false", world.GetLoadedBlock(stone)!.GetProperty("powered"));
        Assert.Equal("true", world.GetLoadedBlock(oak)!.GetProperty("powered"));

        // A mob on the stone plate presses it.
        var living = new PressurePlates.PlateOccupancy(new Dictionary<Vector, int> { [stone] = 1 }, new Dictionary<Vector, int> { [stone] = 1 });
        await plates.ApplyAsync(living);
        Assert.Equal("true", world.GetLoadedBlock(stone)!.GetProperty("powered"));
    }

    [Fact]
    public async Task WeightedPlateTracksItsEntityCountAndReleasesAfterTenTicks()
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 15, 62, 66, 0, 15);
        var plate = new Vector(8, 64, 8);
        await world.Place(plate, BlocksRegistry.Get("minecraft:light_weighted_pressure_plate"));
        var plates = new PressurePlates(world);

        await plates.ApplyAsync(new Dictionary<Vector, int> { [plate] = 3 });
        Assert.Equal("3", world.GetLoadedBlock(plate)!.GetProperty("power"));
        Assert.Contains(world.ScheduledPlateTicks, tick => tick.Position == plate && tick.Delay == 10);
        Assert.Equal(3, world.Engine.GetDirectSignal(plate, BlockFace.Up));

        await plates.ApplyAsync(new Dictionary<Vector, int>());
        await plates.ReleaseAsync(plate);
        Assert.Equal("0", world.GetLoadedBlock(plate)!.GetProperty("power"));
    }

    [Fact]
    public void WeightedPlatesScaleEntityCountToFifteen()
    {
        Assert.Equal(0, PressurePlates.WeightedStrength(0, 15));
        Assert.Equal(1, PressurePlates.WeightedStrength(1, 15));
        Assert.Equal(15, PressurePlates.WeightedStrength(20, 15));
        Assert.Equal(1, PressurePlates.WeightedStrength(1, 150));
        Assert.Equal(8, PressurePlates.WeightedStrength(80, 150));
    }

    [Theory]
    [InlineData(BlockFace.Up, 0.3, "north", "bottom")]
    [InlineData(BlockFace.Down, 0.9, "north", "top")]
    [InlineData(BlockFace.East, 0.8, "east", "top")]
    [InlineData(BlockFace.East, 0.2, "east", "bottom")]
    public void TrapdoorPlacementFollowsTheClickedFaceAndHeight(BlockFace face, double clickedY, string facing, string half)
    {
        // Yaw 0 faces south, so a top or bottom click faces the trapdoor north (away from the player).
        var placed = TrapdoorPlacement.Derive(BlocksRegistry.Get("minecraft:oak_trapdoor"), face, clickedY, 0);
        Assert.Equal(facing, placed.GetProperty("facing"));
        Assert.Equal(half, placed.GetProperty("half"));
    }

    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public async Task PlacedFenceGateFacesThePlayerAndFollowsTheSignal(bool signal, string expected)
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 15, 62, 66, 0, 15);
        await world.Place(new Vector(9, 64, 8), BlocksRegistry.Get("minecraft:cobblestone_wall"));
        ValueTask<IBlock?> Read(Vector p) => ValueTask.FromResult(world.GetLoadedBlock(p));

        var gate = await FenceGatePlacement.DeriveAsync(BlocksRegistry.Get("minecraft:oak_fence_gate"), new Vector(8, 64, 8), 0, signal, Read);

        Assert.Equal("south", gate.GetProperty("facing"));
        Assert.Equal("true", gate.GetProperty("in_wall"));
        Assert.Equal(expected, gate.GetProperty("open"));
        Assert.Equal(expected, gate.GetProperty("powered"));
    }

    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public void PlayerPlacedLampIsLitByASignalAndSetblockKeepsItsState(bool signal, string expected)
    {
        var lamp = BasicRedstoneComponents.DeriveLampPlacement(BlocksRegistry.Get("minecraft:redstone_lamp"), signal);
        Assert.Equal(expected, lamp.GetProperty("lit"));
    }

    [Fact]
    public void PlayerPlacedDoorHalvesOpenTogetherWhenPowered()
    {
        var lower = BlocksRegistry.Get("minecraft:oak_door").WithProperty("half", "lower");
        var upper = BlocksRegistry.Get("minecraft:oak_door").WithProperty("half", "upper");
        var plan = new DoorPlan(new Vector(8, 64, 8), lower, new Vector(8, 65, 8), upper).WithSignal(powered: true);
        Assert.All(new[] { plan.Lower, plan.Upper }, half =>
        {
            Assert.Equal("true", half.GetProperty("open"));
            Assert.Equal("true", half.GetProperty("powered"));
        });
    }

    [Theory]
    [InlineData("minecraft:water", "true")]
    [InlineData("minecraft:air", "false")]
    public void TrapdoorPlacedIntoWaterIsWaterlogged(string current, string expected)
    {
        var trapdoor = BlocksRegistry.Get("minecraft:oak_trapdoor");
        var placed = Waterlogging.Derive(trapdoor, BlocksRegistry.Get(current));
        Assert.Equal(expected, placed.GetProperty("waterlogged"));
    }

    [Fact]
    public async Task PlacedDoorTakesBothHalvesAndRefusesWithoutSpace()
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 15, 62, 66, 0, 15);
        var floor = new Vector(8, 63, 8);
        await world.Place(floor, BlocksRegistry.Get("minecraft:stone"));
        ValueTask<IBlock?> Read(Vector p) => ValueTask.FromResult(world.GetLoadedBlock(p));
        var door = BlocksRegistry.Get("minecraft:oak_door");

        var plan = await DoorPlacement.PlanAsync(door, new Vector(8, 64, 8), BlockFace.Up, 0, Read);
        Assert.NotNull(plan);
        Assert.Equal("lower", plan!.Lower.GetProperty("half"));
        Assert.Equal("upper", plan.Upper.GetProperty("half"));
        Assert.Equal("south", plan.Lower.GetProperty("facing"));

        await world.Place(new Vector(8, 65, 8), BlocksRegistry.Get("minecraft:stone"));
        Assert.Null(await DoorPlacement.PlanAsync(door, new Vector(8, 64, 8), BlockFace.Up, 0, Read));
    }

    [Fact]
    public async Task WallLeverNextToADoorOpensIt()
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 15, 62, 66, 0, 15);
        var lower = new Vector(8, 64, 8);
        var upper = new Vector(8, 65, 8);
        await world.Place(new Vector(8, 63, 8), BlocksRegistry.Get("minecraft:stone"));
        ValueTask<IBlock?> Read(Vector p) => ValueTask.FromResult(world.GetLoadedBlock(p));
        var plan = await DoorPlacement.PlanAsync(BlocksRegistry.Get("minecraft:oak_door"), lower, BlockFace.Up, 0, Read);
        await world.Place(lower, plan!.Lower);
        await world.Place(upper, plan.Upper);

        // Wall lever on the east side of the door, mounted on a stone wall at x=10, pointing west (away from the wall).
        var leverPosition = new Vector(9, 64, 8);
        await world.Place(new Vector(10, 64, 8), BlocksRegistry.Get("minecraft:stone"));
        var lever = BasicRedstoneComponents.DerivePlacementState(BlocksRegistry.Get("minecraft:lever"), BlockFace.West, 0);
        Assert.Equal("wall", lever.GetProperty("face"));
        await world.Place(leverPosition, lever);
        Assert.Equal("false", world.GetLoadedBlock(lower)!.GetProperty("open"));

        // The lever is a source of its own, so turning it on must open the door through the ordinary neighbor path.
        await world.Place(leverPosition, lever.WithProperty("powered", true));

        Assert.Equal("true", world.GetLoadedBlock(lower)!.GetProperty("open"));
        Assert.Equal("true", world.GetLoadedBlock(upper)!.GetProperty("open"));
    }

    [Fact]
    public async Task DoorOpensWhenTheWallBehindItIsPoweredByALeverOnTheFarSide()
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 15, 62, 66, 0, 15);
        var lower = new Vector(8, 64, 8);
        var upper = new Vector(8, 65, 8);
        await world.Place(new Vector(8, 63, 8), BlocksRegistry.Get("minecraft:stone"));
        ValueTask<IBlock?> Read(Vector p) => ValueTask.FromResult(world.GetLoadedBlock(p));
        var plan = await DoorPlacement.PlanAsync(BlocksRegistry.Get("minecraft:oak_door"), lower, BlockFace.Up, 0, Read);
        await world.Place(lower, plan!.Lower);
        await world.Place(upper, plan.Upper);

        // Wall block directly east of the door; the lever is mounted on its far side and points east, away from the wall.
        var wall = new Vector(9, 64, 8);
        var leverPosition = new Vector(10, 64, 8);
        await world.Place(wall, BlocksRegistry.Get("minecraft:stone"));
        await world.Place(new Vector(11, 64, 8), BlocksRegistry.Get("minecraft:stone"));
        var lever = BasicRedstoneComponents.DerivePlacementState(BlocksRegistry.Get("minecraft:lever"), BlockFace.East, 0);
        await world.Place(leverPosition, lever);
        Assert.Equal("false", world.GetLoadedBlock(lower)!.GetProperty("open"));

        // Turning the lever on powers the wall, which powers the door. The lever's own neighbours don't include the door.
        await BasicRedstoneComponents.SetMountedStateAsync(world, leverPosition, lever.WithProperty("powered", true));

        Assert.Equal("true", world.GetLoadedBlock(lower)!.GetProperty("open"));
        Assert.Equal("true", world.GetLoadedBlock(upper)!.GetProperty("open"));
    }

    [Fact]
    public async Task PoweredDoorOpensBothHalves()
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 15, 62, 66, 0, 15);
        var lower = new Vector(8, 64, 8);
        var upper = new Vector(8, 65, 8);
        await world.Place(new Vector(8, 63, 8), BlocksRegistry.Get("minecraft:stone"));
        ValueTask<IBlock?> Read(Vector p) => ValueTask.FromResult(world.GetLoadedBlock(p));
        var plan = await DoorPlacement.PlanAsync(BlocksRegistry.Get("minecraft:oak_door"), lower, BlockFace.Up, 0, Read);
        await world.Place(lower, plan!.Lower);
        await world.Place(upper, plan.Upper);

        await world.Place(new Vector(7, 64, 8), BlocksRegistry.Get("minecraft:redstone_block"));

        Assert.Equal("true", world.GetLoadedBlock(lower)!.GetProperty("open"));
        Assert.Equal("true", world.GetLoadedBlock(upper)!.GetProperty("open"));
    }

    [Fact]
    public async Task NoteBlockPlaysOnlyOnItsRisingEdge()
    {
        var notes = new List<Vector>();
        var world = new RedstoneTestWorld(notes.Add);
        world.Load(0, 15, 62, 66, 0, 15);
        var source = new Vector(7, 64, 8);
        var note = new Vector(8, 64, 8);
        await world.Place(note, BlocksRegistry.Get("minecraft:note_block"));
        await world.Place(source, BlocksRegistry.Get("minecraft:redstone_block"));

        Assert.Equal("true", world.GetLoadedBlock(note)!.GetProperty("powered"));
        Assert.Single(notes);

        await world.Place(source, BlocksRegistry.Air);
        Assert.Equal("false", world.GetLoadedBlock(note)!.GetProperty("powered"));
        Assert.Single(notes);

        await world.Place(source, BlocksRegistry.Get("minecraft:redstone_block"));
        Assert.Equal(2, notes.Count);
    }

    [Fact]
    public async Task RedstoneBlockOpensAndClosesFenceGate()
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 15, 62, 66, 0, 15);
        var source = new Vector(7, 64, 8);
        var gate = new Vector(8, 64, 8);
        await world.Place(gate, BlocksRegistry.Get("minecraft:oak_fence_gate"));
        await world.Place(source, BlocksRegistry.Get("minecraft:redstone_block"));
        Assert.Equal("true", world.GetLoadedBlock(gate)!.GetProperty("powered"));
        Assert.Equal("true", world.GetLoadedBlock(gate)!.GetProperty("open"));

        await world.Place(source, BlocksRegistry.Air);
        Assert.Equal("false", world.GetLoadedBlock(gate)!.GetProperty("powered"));
        Assert.Equal("false", world.GetLoadedBlock(gate)!.GetProperty("open"));
    }

    [Fact]
    public async Task CopperBulbTogglesOnlyOnRisingPowerEdges()
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 15, 62, 66, 0, 15);
        var source = new Vector(7, 64, 8);
        var bulb = new Vector(8, 64, 8);
        await world.Place(source, BlocksRegistry.Get("minecraft:redstone_block"));
        await world.Place(bulb, BlocksRegistry.Get("minecraft:copper_bulb"));
        Assert.Equal("true", world.GetLoadedBlock(bulb)!.GetProperty("powered"));
        Assert.Equal("true", world.GetLoadedBlock(bulb)!.GetProperty("lit"));

        await world.Place(source, BlocksRegistry.Air);
        Assert.Equal("false", world.GetLoadedBlock(bulb)!.GetProperty("powered"));
        Assert.Equal("true", world.GetLoadedBlock(bulb)!.GetProperty("lit"));

        await world.Place(source, BlocksRegistry.Get("minecraft:redstone_block"));
        Assert.Equal("true", world.GetLoadedBlock(bulb)!.GetProperty("powered"));
        Assert.Equal("false", world.GetLoadedBlock(bulb)!.GetProperty("lit"));
    }

    [Fact]
    public async Task WireOutputGoesOnlyToConnectedSidesAndDownward()
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 15, 62, 66, 0, 15);
        world.Redstone.Register(BlockClassOf(Source), new TestSource(signal: (direction, _) => sides.Contains(direction) ? 15 : 0));
        await world.Place(new Vector(4, 64, 8), BlocksRegistry.Get(Source));
        for (var x = 5; x <= 7; x++)
        {
            await world.Place(new Vector(x, 63, 8), BlocksRegistry.Get(Floor));
            await PlaceWire(world, new Vector(x, 64, 8));
        }

        var end = new Vector(7, 64, 8);
        // A one-axis line extends its end on that axis, but it gives nothing on a perpendicular, unconnected side.
        Assert.True(world.Engine.GetSignal(end, BlockFace.West) > 0);
        Assert.Equal(0, world.Engine.GetSignal(end, BlockFace.South));
        Assert.True(world.Engine.GetSignal(new Vector(6, 64, 8), BlockFace.East) > 0);
        // Nothing above the wire; the block beneath it is powered (a wire powers the block it sits on).
        Assert.False(world.Engine.HasSignal(end.Offset(BlockFace.Up), BlockFace.Down));
        Assert.True(world.Engine.HasSignal(new Vector(7, 63, 8), BlockFace.Up));
    }

    [Theory]
    [InlineData("minecraft:sculk_sensor", null)]
    [InlineData("minecraft:calibrated_sculk_sensor", "north")]
    public async Task SculkSensorOutputIsItsPower(string name, string? facing)
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 15, 62, 66, 0, 15);
        var sensor = new Vector(8, 64, 8);
        var block = BlocksRegistry.Get(name).WithProperty("power", 9);
        if (facing is not null)
            block = block.WithProperty("facing", facing);
        await world.Place(sensor, block);

        foreach (var side in sides)
        {
            var expected = facing is not null && side == BlockFace.North ? 0 : 9;
            Assert.Equal(expected, world.Engine.GetSignal(sensor, side));
        }

        Assert.Equal(9, world.Engine.GetDirectSignal(sensor, BlockFace.Down));
        Assert.Equal(0, world.Engine.GetDirectSignal(sensor, BlockFace.Up));
    }

    [Fact]
    public async Task WireNextToSculkSensorTakesItsPower()
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 15, 62, 66, 0, 15);
        await world.Place(new Vector(8, 63, 8), BlocksRegistry.Get(Floor));
        await world.Place(new Vector(9, 63, 8), BlocksRegistry.Get(Floor));
        await world.Place(new Vector(8, 64, 8), BlocksRegistry.Get("minecraft:sculk_sensor").WithProperty("power", 9));
        await PlaceWire(world, new Vector(9, 64, 8));

        Assert.Equal(9, Power(world, new Vector(9, 64, 8)));
    }

    [Fact]
    public async Task BehaviorSignalsAreClampedToTheAnalogRange()
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 15, 62, 66, 0, 15);
        var source = new Vector(8, 64, 8);
        world.Redstone.Register(BlockClassOf(Source), new TestSource(
            signal: (direction, _) => direction == BlockFace.Up ? 99 : -4,
            direct: (direction, _) => direction == BlockFace.Up ? 99 : -4));
        await world.Place(source, BlocksRegistry.Get(Source));

        Assert.Equal(15, world.Engine.GetSignal(source, BlockFace.Up));
        Assert.Equal(0, world.Engine.GetSignal(source, BlockFace.East));
        Assert.Equal(15, world.Engine.GetDirectSignal(source, BlockFace.Up));
        Assert.Equal(0, world.Engine.GetDirectSignal(source, BlockFace.East));
    }

    [Fact]
    public void InstallingTwiceIsRejected()
    {
        var world = new RedstoneTestWorld();
        Assert.Throws<InvalidOperationException>(() => world.Redstone.Register(RedstoneWire.BlockClass, new TestSource()));
        Assert.Throws<InvalidOperationException>(() => RedstoneRegistration.Install(world, world.BlockTicks, world.Neighbors, world.Redstone));
    }

    private static string BlockClassOf(string name) => BlocksRegistry.Get(name).BlockClass();

    private static int Power(RedstoneTestWorld world, Vector position) => int.Parse(world.GetLoadedBlock(position)!.GetProperty("power"));

    private static async Task PlaceWire(RedstoneTestWorld world, Vector position) =>
        await world.Place(position, BlocksRegistry.Get("minecraft:redstone_wire"));

    private static async Task BuildLine(RedstoneTestWorld world, Vector source, Vector[] wires, bool sourceFirst)
    {
        world.Load(0, 15, 62, 66, 0, 15);
        world.Redstone.Register(BlockClassOf(Source), new TestSource(signal: (direction, _) => sides.Contains(direction) ? 15 : 0));
        if (sourceFirst)
            await world.Place(source, BlocksRegistry.Get(Source));

        foreach (var wire in wires)
        {
            await world.Place(new Vector(wire.X, 63, wire.Z), BlocksRegistry.Get(Floor));
            await PlaceWire(world, wire);
        }

        if (!sourceFirst)
            await world.Place(source, BlocksRegistry.Get(Source));
    }

    private static string Dump(RedstoneTestWorld world, Vector[] wires) =>
        string.Join(";", wires.Select(wire =>
        {
            var state = world.GetLoadedBlock(wire)!;
            return $"{wire}:{state.GetProperty("power")}:{state.GetProperty("east")}:{state.GetProperty("west")}";
        }));

    /// <summary>
    /// A source whose signals are given per direction, where <c>direction</c> is the direction from the receiver to the source.
    /// </summary>
    private sealed class TestSource(Func<BlockFace, Vector, int>? signal = null, Func<BlockFace, Vector, int>? direct = null) : IRedstoneBehavior
    {
        public int GetSignal(IBlock state, Vector position, BlockFace direction, bool wiresGiveSignal) =>
            signal?.Invoke(direction, position) ?? 0;

        public int GetDirectSignal(IBlock state, Vector position, BlockFace direction, bool wiresGiveSignal) =>
            direct?.Invoke(direction, position) ?? 0;
    }
}
