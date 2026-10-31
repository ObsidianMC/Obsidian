using Obsidian.API;
using Obsidian.API.Registries;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Registries;
using Obsidian.Tests.Utilities;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Generators.Features;
using Obsidian.WorldData.Generators.Mojang;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Generators.Mojang.Features;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Where the dragon fight finds its exit portal, or builds a new one (<see cref="ExitPortalSearch"/>), on an End
/// stone island with fountains placed by the 26.3 <c>end_podium</c> feature, which matches vanilla's captures.
/// </summary>
public class EndExitPortal
{
    private const int MinY = 0;

    private const int Height = 256;

    private sealed record Case(
        string Name,
        int Ground,
        (Vector Origin, bool Active)[] Fountains,
        (Vector Position, IBlock Block)[] Edits,
        Vector? Found,
        Vector SpawnOrigin,
        bool SearchesPortals = false);

    private static readonly Case[] cases =
    [
        new("untouched island", 63, [], [], null, new Vector(0, 63, 0)),
        new("low island", 5, [], [], null, new Vector(0, 5, 0)),
        new("no island", -1, [], [], null, new Vector(0, MinY + 1, 0)),
        new("bedrock above y 63", 63, [], Column(64, 70, BlocksRegistry.Bedrock), null, new Vector(0, 63, 0)),
        new("bedrock below y 63", 48, [], Column(49, 50, BlocksRegistry.Bedrock), null, new Vector(0, 50, 0)),
        new("inactive fountain", 63, [(new Vector(0, 63, 0), false)], [], new Vector(0, 63, 0), new Vector(0, 63, 0)),
        new(
            "block above a fountain",
            63,
            [(new Vector(0, 63, 0), false)],
            [(new Vector(0, 70, 0), BlocksRegistry.EndStone)],
            new Vector(0, 63, 0),
            new Vector(0, 70, 0)),
        new(
            "fountain missing a rim block",
            63,
            [(new Vector(0, 63, 0), false)],
            [(new Vector(3, 63, 0), BlocksRegistry.EndStone)],
            null,
            new Vector(0, 63, 0)),
        new(
            "fountain missing a floor block",
            63,
            [(new Vector(0, 63, 0), false)],
            [(new Vector(2, 62, 1), BlocksRegistry.EndStone)],
            null,
            new Vector(0, 63, 0)),
        new(
            "active fountain off the centre column",
            40,
            [(new Vector(20, 41, 20), true)],
            [],
            new Vector(20, 41, 20),
            new Vector(0, 40, 0),
            SearchesPortals: true),
        new(
            "inactive fountain off the centre column",
            40,
            [(new Vector(20, 41, 20), false)],
            [],
            null,
            new Vector(0, 40, 0))
    ];

    [Fact]
    public void FindsAndPlacesTheExitPortalLikeVanilla()
    {
        // The search reads the end portals' block entities, so the block must have one.
        Assert.Equal("minecraft:end_portal", BlocksRegistry.EndPortal.BlockEntityType());

        FixtureCases.AssertAll(cases, testCase => testCase.Name, AssertCase);
    }

    private static void AssertCase(Case testCase)
    {
        var chunks = Island(testCase.Ground);
        var region = new WorldGenRegion(chunks, 0, 0, 0L, MinY, Height, 0,
            new FixedBiomeSource(CodecRegistry.Biomes.TheEnd), new XoroshiroRandomSource(0L), false);

        foreach (var (origin, active) in testCase.Fountains)
            PlaceFountain(region, origin, active);

        foreach (var (position, block) in testCase.Edits)
            region.SetBlock(position, block);

        var portals = testCase.SearchesPortals ? Portals(region) : [];
        IBlock BlockAt(Vector position) => region.GetBlock(position);
        var blocking = ExitPortalSearch.Surface(BlockAt, HeightmapType.MotionBlocking, MinY, Height);
        var surface = ExitPortalSearch.Surface(BlockAt, HeightmapType.MotionBlockingNoLeaves, MinY, Height);

        if (testCase.SearchesPortals)
        {
            // The centre column's search alone doesn't reach the fountain.
            Assert.NotEmpty(portals);
            Assert.Null(ExitPortalSearch.Find(BlockAt, [], blocking, MinY));
        }

        var found = ExitPortalSearch.Find(BlockAt, portals, blocking, MinY);
        var spawn = ExitPortalSearch.SpawnOrigin(BlockAt, surface, MinY);

        Assert.Equal(testCase.Found, found);
        Assert.Equal(testCase.SpawnOrigin, spawn);
    }

    private static (Vector Position, IBlock Block)[] Column(int fromY, int toY, IBlock block) =>
        Enumerable.Range(fromY, toY - fromY + 1).Select(y => (new Vector(0, y, 0), block)).ToArray();

    // The 3x3 chunks around the centre, End stone up to the ground's y.
    private static Dictionary<(int X, int Z), IChunk> Island(int ground)
    {
        var chunks = new Dictionary<(int X, int Z), IChunk>();

        for (var chunkZ = -1; chunkZ <= 1; chunkZ++)
        {
            for (var chunkX = -1; chunkX <= 1; chunkX++)
            {
                var chunk = new Chunk(chunkX, chunkZ, MinY, Height);
                Fill(chunk, ground);
                chunks.Add((chunkX, chunkZ), chunk);
            }
        }

        return chunks;
    }

    private static void Fill(Chunk chunk, int ground)
    {
        for (var y = MinY; y <= ground; y++)
        {
            for (var z = 0; z < 16; z++)
            {
                for (var x = 0; x < 16; x++)
                    chunk.SetBlock(x, y, z, BlocksRegistry.EndStone);
            }
        }
    }

    private static void PlaceFountain(WorldGenRegion region, Vector origin, bool active)
    {
        var registry = new FeatureRegistry(WorldgenData.Vanilla);
        var definition = new JsonObject
        {
            ["type"] = "minecraft:end_podium",
            ["active"] = active
        };
        var podium = registry.Feature(JsonSerializer.SerializeToElement(definition));
        var context = new DecorationContext(registry, region, _ => "minecraft:the_end", MinY, Height);

        Assert.True(podium(context, region.Random, origin));
    }

    // The end portal blocks the fountains hold, standing in for their block entities.
    private static Vector[] Portals(WorldGenRegion region)
    {
        var portals = new List<Vector>();

        foreach (var position in FeatureHelpers.BetweenClosed(new Vector(-16, MinY, -16), new Vector(31, 127, 31)))
        {
            if (region.GetBlock(position).Is(BlockIds.EndPortal))
                portals.Add(position);
        }

        return portals.ToArray();
    }
}
