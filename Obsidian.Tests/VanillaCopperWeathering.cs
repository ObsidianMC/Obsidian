using Obsidian.API;
using Obsidian.API.BlockEntities;
using Obsidian.API.Blocks;
using Obsidian.API.World.Generator.RandomSources;
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

public sealed class VanillaCopperWeathering(ITestOutputHelper output)
{
    [Fact]
    public async Task ReplayWeatheringReference()
    {
        using var stream = typeof(VanillaCopperWeathering).Assembly
            .GetManifestResourceStream("Obsidian.Tests.Assets.copper_weathering.json")!;
        using var document = JsonDocument.Parse(stream);
        Assert.Equal(ServerConstants.ProtocolDescription, document.RootElement.GetProperty("version").GetString());
        var cases = document.RootElement.GetProperty("cases").EnumerateArray().ToArray();

        await using var context = TestServerFactory.CreateServer($"copper-weathering-{Guid.NewGuid():N}");
        await BlockUpdates.AddChunksAsync(context.World);
        var world = context.World;
        var positions = cases.SelectMany(probe => probe.GetProperty("setup").EnumerateArray())
            .Select(placement => Position(placement.GetProperty("pos")))
            .Distinct()
            .ToArray();

        FixtureCases.AssertAll(cases, probe => probe.GetProperty("name").GetString()!, probe =>
        {
            // Reuse the loaded block view. Setup and cleanup suppress unrelated placement and shape reactions.
            foreach (var position in positions)
            {
                world.BlockUpdates.SetBlock(position, BlocksRegistry.Air,
                    BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace);
            }

            foreach (var placement in probe.GetProperty("setup").EnumerateArray())
            {
                world.BlockUpdates.SetBlock(Position(placement.GetProperty("pos")),
                    VanillaSulfurBlocks.ParseState(placement.GetProperty("state").GetString()!),
                    BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace);
            }

            var origin = Position(probe.GetProperty("origin"));
            if (probe.TryGetProperty("missingEntity", out var missing) && missing.GetBoolean())
                world.GetLoadedChunk(origin.X >> 4, origin.Z >> 4)!.RemoveBlockEntity(origin.X, origin.Y, origin.Z);

            var entity = world.GetResidentBlockEntity(origin) as ContainerBlockEntity;
            if (probe.TryGetProperty("open", out var open) && open.GetBoolean())
                entity!.Inventory.Viewers.Add(null!);

            var entities = positions.ToDictionary(position => position, world.GetResidentBlockEntity);
            var state = world.BlockUpdates.GetBlock(origin);
            var random = new TracedRandom(probe.GetProperty("seed").GetInt64());
            var ticking = BlockPhysics.IsRandomlyTicking(state.GetHashCode());
            Assert.Equal(probe.GetProperty("randomlyTicking").GetBoolean(), ticking);

            // Use the generated owner dispatch, with the same per-state gate as TickChunkBlocks.
            if (ticking)
            {
                var behavior = BlocksRegistry.GetRandomTickBehavior(state.RegistryId);
                Assert.NotNull(behavior);
                behavior.RandomTick(world, origin, state, random);
            }

            var draws = probe.GetProperty("draws").EnumerateArray().ToArray();
            Assert.Equal(draws.Length, random.Draws.Count);
            for (var index = 0; index < draws.Length; index++)
            {
                Assert.Equal("nextFloat", draws[index].GetProperty("method").GetString());
                Assert.Equal(24, random.Draws[index].Bits);
                Assert.Equal(draws[index].GetProperty("value").GetSingle(), random.Draws[index].Value / 16777216f);
            }

            Assert.Equal(probe.GetProperty("nextRandom").GetInt32(), random.NextInt());
            foreach (var expected in probe.GetProperty("result").EnumerateArray())
            {
                var position = Position(expected.GetProperty("pos"));
                var wanted = VanillaSulfurBlocks.ParseState(expected.GetProperty("state").GetString()!);
                var actual = world.BlockUpdates.GetBlock(position);
                Assert.True(wanted.IsSameState(actual), $"At {position}: expected {expected}, got {actual}");
                if (expected.TryGetProperty("sameBlockEntity", out var retained))
                {
                    Assert.True(retained.GetBoolean());
                    Assert.True(expected.GetProperty("acceptsState").GetBoolean());
                    Assert.NotNull(entities[position]);
                    Assert.Same(entities[position], world.GetResidentBlockEntity(position));
                    Assert.Equal(expected.GetProperty("blockEntityType").GetString(), entities[position]!.Id);
                }
            }
        });

        output.WriteLine($"Replayed {cases.Length} copper weathering cases.");
    }

    private static Vector Position(JsonElement values) =>
        new(values[0].GetInt32(), values[1].GetInt32(), values[2].GetInt32());

    private sealed class TracedRandom(long seed) : LegacyRandomSource(seed)
    {
        public List<(int Bits, int Value)> Draws { get; } = [];

        public override int Next(int bits)
        {
            var value = base.Next(bits);
            this.Draws.Add((bits, value));
            return value;
        }
    }
}
