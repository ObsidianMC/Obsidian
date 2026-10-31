using Obsidian.API;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Commands;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Tests.Fakes;
using Obsidian.Utilities.Converters;
using Obsidian.WorldData;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Obsidian.Tests;

public sealed class ProjectilePersistence(ITestOutputHelper output)
{
    internal static async Task ReplayVanillaReleaseTransition(JsonElement probe, EntityType type)
    {
        await using var context = TestServerFactory.CreateServer($"arrow-release-{Guid.NewGuid():N}");
        var world = context.World;
        await BlockUpdates.AddChunksAsync(world);
        using var simulation = world.AddChunkTicket(0, 0);
        var id = type switch
        {
            EntityType.Trident => "minecraft:trident",
            EntityType.SpectralArrow => "minecraft:spectral_arrow",
            _ => "minecraft:arrow"
        };
        var saved = new NbtCompound
        {
            new NbtTag<string>("id", id),
            EntityNbt.DoubleList("Pos", Location(probe.GetProperty("position"))),
            EntityNbt.DoubleList("Motion", Location(probe.GetProperty("motion"))),
            new NbtTag<bool>("inGround", true),
            new NbtTag<short>("life", probe.GetProperty("life").GetInt16())
        };
        if (probe.TryGetProperty("savedState", out var state))
        {
            if (state.ValueKind == JsonValueKind.Object && state.TryGetProperty("", out var wrapped))
            {
                var tag = BlockStateNbt.WrapPaletteEntry(JsonNbt.ToNbt("", wrapped));
                tag.Name = "inBlockState";
                saved.Set(tag);
            }
            else
            {
                saved.Set(JsonNbt.ToNbt("inBlockState", state));
            }
        }

        var arrow = Assert.IsAssignableFrom<Arrow>(EntityNbt.Load(saved, world));
        var before = EntityNbt.Save(arrow)!;
        var decoded = probe.GetProperty("decodedState");
        if (decoded.ValueKind == JsonValueKind.Null)
        {
            Assert.False(before.HasTag("inBlockState"));
        }
        else
        {
            Assert.Equal(Snbt.Write(JsonNbt.ToNbt("inBlockState", decoded)), Snbt.Write(before["inBlockState"]));
        }

        var seed = probe.GetProperty("seed").GetInt64();
        var drawSource = new LegacyRandomSource(seed);
        foreach (var draw in probe.GetProperty("draws").EnumerateArray())
        {
            Assert.Equal(draw.GetSingle(), drawSource.NextFloat());
        }

        arrow.ReleaseRandom = new LegacyRandomSource(seed);
        await arrow.TickAsync();
        var after = EntityNbt.Save(arrow)!;
        Assert.False(arrow.NoGravity);
        Assert.Equal(Location(probe.GetProperty("releasedPosition")), arrow.Position);
        Assert.Equal(Location(probe.GetProperty("releasedMotion")), arrow.Motion);
        Assert.True(after.TryGetTagValue<bool>("inGround", out var embedded));
        Assert.Equal(probe.GetProperty("inGround").GetBoolean(), embedded);
        Assert.Equal(probe.GetProperty("releasedLife").GetInt32(), after.GetShort("life"));
        Assert.Equal(probe.GetProperty("nextRandom").GetInt32(), arrow.ReleaseRandom.NextInt());
    }

    [Fact]
    public async Task EmbeddedProjectileKeepsItsReleaseBehaviorAfterRoundTrip()
    {
        var failures = new List<string>();
        var cases = 0;
        foreach (var type in new[] { EntityType.Arrow, EntityType.SpectralArrow, EntityType.Trident })
        {
            foreach (var format in new[] { "vanilla", "missing", "custom" })
            {
                cases++;
                await VanillaSulfurBlocks.CollectFailureAsync(failures, $"{type}/{format}",
                    () => CheckSavedProjectile(type, format));
            }
        }

        output.WriteLine($"Replayed {cases} projectile save/load cases.");
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    private static async Task CheckSavedProjectile(EntityType type, string format)
    {
        await using var context = TestServerFactory.CreateServer($"projectile-save-{Guid.NewGuid():N}");
        var world = context.World;
        await BlockUpdates.AddChunksAsync(world);
        using var simulation = world.AddChunkTicket(0, 0);
        using var neighboringSimulation = world.AddChunkTicket(-1, 0);
        var target = new Vector(0, 72, 1);
        await world.SetBlockAsync(target, VanillaSulfurBlocks.ParseState("minecraft:stone"));
        Arrow original = type switch
        {
            EntityType.Trident => new Trident { Level = world },
            EntityType.SpectralArrow => new SpectralArrow { Level = world },
            _ => new Arrow { Level = world }
        };
        original.Type = type;
        original.EntityId = Obsidian.Server.GetNextEntityId();
        original.Position = new VectorD(-0.9, 72.5, 1.5);
        original.Motion = new VectorD(2, 0, 0);
        original.NoGravity = true;
        Assert.True(world.TryAddEntity(original));
        await original.TickAsync();
        var saved = EntityNbt.Save(original)!;
        Assert.True(saved.TryGetTagValue<bool>("inGround", out var embedded) && embedded);
        Assert.Equal("minecraft:stone", Assert.IsType<NbtTag<string>>(saved["inBlockState"]).Value);
        Assert.False(saved.HasTag("ObsidianEmbeddedBlock"));
        if (format != "vanilla")
        {
            saved.Remove("inBlockState");
        }

        if (format == "custom")
        {
            // Previous Obsidian saves may retain the custom cell field; it must not control release.
            saved.Set(new NbtArray<int>("ObsidianEmbeddedBlock", [target.X, target.Y, target.Z]));
        }

        var restored = Assert.IsAssignableFrom<Arrow>(EntityNbt.Load(saved, world));
        Assert.Equal(original.GetType(), restored.GetType());
        Assert.Equal(original.Position, restored.Position);

        foreach (var arrow in new[] { original, restored })
        {
            await arrow.TickAsync();
            Assert.True(EntityNbt.Save(arrow)!.TryGetTagValue<bool>("inGround", out embedded) && embedded);
        }

        await world.SetBlockAsync(target + Vector.Down, VanillaSulfurBlocks.ParseState("minecraft:stone"));
        await world.SetBlockAsync(target, VanillaSulfurBlocks.ParseState("minecraft:short_grass"));
        foreach (var arrow in new[] { original, restored })
        {
            await arrow.TickAsync();
            Assert.True(EntityNbt.Save(arrow)!.TryGetTagValue<bool>("inGround", out embedded));
            Assert.False(embedded);
        }

        Assert.Equal(original.Position, restored.Position);
        Assert.Equal(original.Motion, restored.Motion);
        Assert.False(EntityNbt.Save(restored)!.HasTag("ObsidianEmbeddedBlock"));
        Assert.True(await world.DestroyEntityAsync(original));
        Assert.Null(world.FindEntity(original.Uuid));
    }

    internal static async Task VanillaBlockStateSurvivesRoundTripAndReleases(JsonElement probe, EntityType type)
    {
        var state = probe.GetProperty("savedState");
        var expected = JsonNbt.ToNbt("inBlockState", state);
        await using var context = TestServerFactory.CreateServer($"vanilla-arrow-save-{Guid.NewGuid():N}");
        var world = context.World;
        await BlockUpdates.AddChunksAsync(world);
        using var simulation = world.AddChunkTicket(0, 0);
        var target = new Vector(1, 72, 1);
        await world.SetBlockAsync(target + Vector.Down, VanillaSulfurBlocks.ParseState("minecraft:stone"));
        await world.SetBlockAsync(target,
            VanillaSulfurBlocks.ParseState(probe.GetProperty("setup")[0].GetProperty("state").GetString()!));

        var id = type switch
        {
            EntityType.Trident => "minecraft:trident",
            EntityType.SpectralArrow => "minecraft:spectral_arrow",
            _ => "minecraft:arrow"
        };
        var vanilla = new NbtCompound
        {
            new NbtTag<string>("id", id),
            EntityNbt.DoubleList("Pos", new VectorD(1.5, 72.5, 1.5)),
            EntityNbt.DoubleList("Motion", VectorD.Zero),
            new NbtTag<bool>("inGround", true),
            new NbtTag<bool>("NoGravity", true),
            expected
        };
        var loaded = Assert.IsAssignableFrom<Arrow>(EntityNbt.Load(vanilla, world));
        var saved = EntityNbt.Save(loaded)!;
        Assert.Equal(Snbt.Write(expected), Snbt.Write(saved["inBlockState"]));
        Assert.False(saved.HasTag("ObsidianEmbeddedBlock"));
        var restored = Assert.IsAssignableFrom<Arrow>(EntityNbt.Load(saved, world));

        foreach (var arrow in new[] { loaded, restored })
        {
            await arrow.TickAsync();
            var again = EntityNbt.Save(arrow)!;
            Assert.Equal(Snbt.Write(expected), Snbt.Write(again["inBlockState"]));
            Assert.True(again.TryGetTagValue<bool>("inGround", out var embedded) && embedded);
        }

        await world.SetBlockAsync(target, VanillaSulfurBlocks.ParseState("minecraft:fern"));
        foreach (var arrow in new[] { loaded, restored })
        {
            await arrow.TickAsync();
            Assert.True(EntityNbt.Save(arrow)!.TryGetTagValue<bool>("inGround", out var embedded));
            Assert.False(embedded);
        }
    }

    private static VectorD Location(JsonElement values) =>
        new(values[0].GetDouble(), values[1].GetDouble(), values[2].GetDouble());
}
