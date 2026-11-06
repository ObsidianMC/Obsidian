using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.WorldData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Obsidian.Tests;

/// <summary>
/// Replays vanilla's placement of every block item (<c>Assets/block_placement.json</c>, recorded by
/// <c>PlacementFixtures.java</c>) through the use-item-on packet players send, in one shared world.
/// </summary>
public sealed class VanillaBlockPlacement(ITestOutputHelper output)
{
    // How far from the target the compared area reaches, as in the capture.
    private const int Reach = 2;

    // Setup and clearing, like the capture's: no neighbor or shape updates and nothing placed reacting.
    private const BlockUpdateFlags Quiet = BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace;

    // Items whose block vanilla places with an age drawn from the level's random (GrowingPlantHeadBlock), which
    // Obsidian draws from its own: their states are compared without the age.
    private static readonly HashSet<string> randomAge =
    [
        "minecraft:kelp",
        "minecraft:weeping_vines",
        "minecraft:twisting_vines",
        "minecraft:glow_berries"
    ];

    private sealed record Setup(Vector Offset, IBlock State);

    private sealed record Context(
        string Name,
        Setup[] Setup,
        Vector Clicked,
        BlockFace Face,
        VectorF Cursor,
        float Yaw,
        float Pitch,
        bool Sneaking);

    private sealed record Case(string Item, Context Context, string Expected);

    [Fact]
    public async Task ReplayBlockItemPlacementReference()
    {
        using var stream = typeof(VanillaBlockPlacement).Assembly
            .GetManifestResourceStream("Obsidian.Tests.Assets.block_placement.json")!;
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        Assert.Equal(ServerConstants.ProtocolDescription, root.GetProperty("version").GetString());

        var target = Position(root.GetProperty("target"));
        var contexts = root.GetProperty("contexts").EnumerateArray().Select(ReadContext).ToArray();
        var cases = root.GetProperty("items").EnumerateObject()
            .SelectMany(item => ReadCases(item, contexts))
            .ToArray();

        await using var context = TestServerFactory.CreateServer($"block-placement-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(context.World);

        // Loaded as the packet loads it, so the first case reads the area as the rest do.
        await context.World.GetBlockAsync(target);
        var player = TestServerFactory.CreateConnectedPlayer(
            context.Server,
            context.World,
            "Placer",
            Location(root.GetProperty("player")));
        player.GameMode = GameMode.Survival;

        await FixtureCases.AssertAllAsync(
            cases,
            placement => $"{placement.Item} in {placement.Context.Name}",
            placement => ReplayAsync(context, player, target, placement));
        output.WriteLine($"Replayed {cases.Length} placements.");
    }

    private static async Task ReplayAsync(RealServerContext context, Obsidian.Entities.Player player, Vector target,
        Case placement)
    {
        var world = context.World;
        var setting = placement.Context;

        // A cleared area with the context's blocks, and a fresh stack of two of the item in the player's hand.
        Clear(world, target);
        foreach (var setup in setting.Setup)
            world.BlockUpdates.SetBlock(target + setup.Offset, setup.State, Quiet);

        var before = Snapshot(world, target);
        player.Yaw = setting.Yaw;
        player.Pitch = setting.Pitch;
        player.Sneaking = setting.Sneaking;
        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.Get(placement.Item), 2));

        // The player uses it, and every block update it caused runs.
        var packet = BlockUpdates.UseOn(target + setting.Clicked, setting.Face, setting.Cursor);
        await packet.HandleAsync(context.Server, player);
        await world.BlockNotifications.DrainAsync();
        ((TestClient)player.Client).ClearPackets();

        var withoutAge = randomAge.Contains(placement.Item);
        var changes = Changes(world, target, before, withoutAge);
        var expected = withoutAge ? WithoutAge(placement.Expected) : placement.Expected;
        Assert.True(changes == expected, $"expected '{expected}', got '{changes}'");
    }

    private static void Clear(WorldData.World world, Vector target)
    {
        for (var y = -Reach - 1; y <= Reach + 1; y++)
        {
            for (var z = -Reach - 1; z <= Reach + 1; z++)
            {
                for (var x = -Reach - 1; x <= Reach + 1; x++)
                    world.BlockUpdates.SetBlock(target + new Vector(x, y, z), BlocksRegistry.Air, Quiet);
            }
        }
    }

    private static IBlock[] Snapshot(WorldData.World world, Vector target) =>
        Area(target).Select(world.BlockUpdates.GetBlock).ToArray();

    // Each block of the area that changed, as dx,dy,dz=state relative to the target, the way the capture writes them.
    private static string Changes(WorldData.World world, Vector target, IBlock[] before, bool withoutAge)
    {
        var changed = new List<string>();
        var index = 0;
        foreach (var position in Area(target))
        {
            var state = world.BlockUpdates.GetBlock(position);
            if (!state.IsSameState(before[index]))
            {
                var offset = position - target;
                var compared = withoutAge ? AgeZero(state) : state;
                changed.Add($"{offset.X},{offset.Y},{offset.Z}={Text(compared)}");
            }

            index++;
        }

        return string.Join(';', changed);
    }

    // A captured result with every state's age at zero.
    private static string WithoutAge(string result)
    {
        if (result.Length == 0)
            return result;

        var changes = result.Split(';').Select(change =>
        {
            var separator = change.IndexOf('=');
            var state = VanillaSulfurBlocks.ParseState(change[(separator + 1)..]);
            return $"{change[..separator]}={Text(AgeZero(state))}";
        });

        return string.Join(';', changes);
    }

    private static IBlock AgeZero(IBlock state) => state.HasProperty("age") ? state.WithProperty("age", 0) : state;

    // The compared area in the capture's order: y, then z, then x ascending.
    private static IEnumerable<Vector> Area(Vector target)
    {
        for (var y = -Reach; y <= Reach; y++)
        {
            for (var z = -Reach; z <= Reach; z++)
            {
                for (var x = -Reach; x <= Reach; x++)
                    yield return target + new Vector(x, y, z);
            }
        }
    }

    // The block and the properties it doesn't share with its default state, sorted by name like vanilla's.
    private static string Text(IBlock state)
    {
        var defaults = BlockStateProperties.GetState(state.UnlocalizedName).GetProperties();
        var properties = state.GetProperties()
            .Where(pair => defaults.GetValueOrDefault(pair.Key) != pair.Value)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}={pair.Value}")
            .ToArray();

        if (properties.Length == 0)
            return state.UnlocalizedName;

        return $"{state.UnlocalizedName}[{string.Join(',', properties)}]";
    }

    private static IEnumerable<Case> ReadCases(JsonProperty item, Context[] contexts)
    {
        var results = item.Value.EnumerateArray().Select(result => result.GetString()!).ToArray();
        Assert.Equal(contexts.Length, results.Length);

        return results.Select((result, index) => new Case(item.Name, contexts[index], result));
    }

    private static Context ReadContext(JsonElement context)
    {
        var setup = context.GetProperty("setup").EnumerateArray()
            .Select(block => new Setup(
                Position(block.GetProperty("offset")),
                VanillaSulfurBlocks.ParseState(block.GetProperty("state").GetString()!)))
            .ToArray();
        var cursor = Location(context.GetProperty("cursor"));

        return new Context(
            context.GetProperty("name").GetString()!,
            setup,
            Position(context.GetProperty("clicked")),
            Enum.Parse<BlockFace>(context.GetProperty("face").GetString()!, ignoreCase: true),
            new VectorF((float)cursor.X, (float)cursor.Y, (float)cursor.Z),
            context.GetProperty("yaw").GetSingle(),
            context.GetProperty("pitch").GetSingle(),
            context.GetProperty("sneaking").GetBoolean());
    }

    private static Vector Position(JsonElement values) =>
        new(values[0].GetInt32(), values[1].GetInt32(), values[2].GetInt32());

    private static VectorD Location(JsonElement values) =>
        new(values[0].GetDouble(), values[1].GetDouble(), values[2].GetDouble());
}
