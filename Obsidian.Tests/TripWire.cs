using System.Threading.Tasks;
using Obsidian.API;
using Obsidian.Registries;
using Obsidian.WorldData.Redstone;
using Xunit;

namespace Obsidian.Tests;

public class TripWireTests
{
    private static readonly Vector HookA = new(8, 64, 8);
    private static readonly Vector HookB = new(12, 64, 8);
    private static readonly Vector Wire = new(10, 64, 8);

    // Stone supports on the far side of each hook, a hook facing east at A and west at B, and three wires between them.
    private static async Task<RedstoneTestWorld> BuildLineAsync()
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 15, 62, 66, 0, 15, ringChunks: 1);
        var stone = BlocksRegistry.Get("minecraft:stone");
        await world.Place(new Vector(7, 64, 8), stone);
        await world.Place(new Vector(13, 64, 8), stone);
        await world.Place(HookA, Hook("east"));
        await world.Place(HookB, Hook("west"));
        for (var x = 9; x <= 11; x++)
            await world.Place(new Vector(x, 64, 8), BlocksRegistry.Get("minecraft:tripwire"));
        return world;
    }

    private static IBlock Hook(string facing) =>
        BlocksRegistry.Get("minecraft:tripwire_hook").WithProperty("facing", facing).WithProperty("attached", "false").WithProperty("powered", "false");

    [Fact]
    public async Task HooksAttachWhenALineOfTripwireConnectsThem()
    {
        var world = await BuildLineAsync();

        Assert.Equal("true", world.GetLoadedBlock(HookA)!.GetProperty("attached"));
        Assert.Equal("true", world.GetLoadedBlock(HookB)!.GetProperty("attached"));
        Assert.Equal("false", world.GetLoadedBlock(HookA)!.GetProperty("powered"));
    }

    [Fact]
    public async Task TouchingATripwirePowersTheLineAndTheHookSignalsItsSupport()
    {
        var world = await BuildLineAsync();

        await world.Tripwires.ApplyContactsAsync([Wire]);

        Assert.Equal("true", world.GetLoadedBlock(Wire)!.GetProperty("powered"));
        Assert.Equal("true", world.GetLoadedBlock(HookA)!.GetProperty("powered"));
        Assert.Equal("true", world.GetLoadedBlock(HookB)!.GetProperty("powered"));
        // Vanilla getDirectSignal: the hook gives 15 to the block it is attached to, on its facing side only.
        Assert.Equal(15, world.Engine.GetDirectSignal(HookA, BlockFace.East));
        Assert.Equal(0, world.Engine.GetDirectSignal(HookA, BlockFace.West));
    }

    [Fact]
    public async Task APoweredWireRechecksAfterTenTicksAndReleasesWhenNothingTouchesIt()
    {
        var world = await BuildLineAsync();
        await world.Tripwires.ApplyContactsAsync([Wire]);

        // Still touched at the recheck: stays powered.
        for (var tick = 0; tick < 10; tick++)
            await world.Advance();
        Assert.Equal("true", world.GetLoadedBlock(Wire)!.GetProperty("powered"));

        // Nothing touches it at the next recheck: it releases, and the hooks follow.
        await world.Tripwires.ApplyContactsAsync([]);
        for (var tick = 0; tick < 12; tick++)
            await world.Advance();
        Assert.Equal("false", world.GetLoadedBlock(Wire)!.GetProperty("powered"));
        Assert.Equal("false", world.GetLoadedBlock(HookA)!.GetProperty("powered"));
        Assert.Equal("false", world.GetLoadedBlock(HookB)!.GetProperty("powered"));
    }

    [Fact]
    public async Task RemovingTheFarHookDetachesTheLine()
    {
        var world = await BuildLineAsync();

        await world.Place(HookB, BlocksRegistry.Air);

        Assert.Equal("false", world.GetLoadedBlock(HookA)!.GetProperty("attached"));
    }

    [Fact]
    public async Task AHookWithNoSturdySupportIsRefused()
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 15, 62, 66, 0, 15, ringChunks: 1);

        var placed = await TripWireHook.DeriveAsync(Hook("east"), HookA, BlockFace.East, 0f, _ => ValueTask.FromResult<IBlock?>(BlocksRegistry.Air));

        Assert.Null(placed);
    }
}
