using System.Threading.Tasks;
using Obsidian.API;
using Obsidian.Registries;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// The directional power engine at an unloaded chunk edge. <see cref="VanillaRedstone"/> replays vanilla's behavior;
/// this covers what the captures can't: a block update beside an unloaded chunk must not load it.
/// </summary>
public class RedstoneEngineTests
{
    private const string Floor = "minecraft:stone";

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

    private static int Power(RedstoneTestWorld world, Vector position) => int.Parse(world.GetLoadedBlock(position)!.GetProperty("power"));

    private static async Task PlaceWire(RedstoneTestWorld world, Vector position) =>
        await world.Place(position, BlocksRegistry.Get("minecraft:redstone_wire"));
}
