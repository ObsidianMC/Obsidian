using Obsidian.API;
using Obsidian.Commands;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Tests.Fakes;
using Obsidian.Utilities.Converters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public sealed class ProjectilePersistence
{
    private static readonly JsonElement savedStates = LoadSavedStates();

    public static IEnumerable<object[]> VanillaSavedStates()
    {
        foreach (var probe in savedStates.EnumerateArray()
            .Where(probe => probe.GetProperty("kind").GetString() == "arrow_saved_state"))
        {
            foreach (var type in new[] { EntityType.Arrow, EntityType.SpectralArrow, EntityType.Trident })
            {
                yield return [probe.GetProperty("name").GetString()!, type];
            }
        }
    }

    [Theory]
    [InlineData(EntityType.Arrow, "vanilla")]
    [InlineData(EntityType.SpectralArrow, "vanilla")]
    [InlineData(EntityType.Trident, "vanilla")]
    [InlineData(EntityType.Arrow, "missing")]
    [InlineData(EntityType.SpectralArrow, "missing")]
    [InlineData(EntityType.Trident, "missing")]
    [InlineData(EntityType.Arrow, "custom")]
    [InlineData(EntityType.SpectralArrow, "custom")]
    [InlineData(EntityType.Trident, "custom")]
    public async Task EmbeddedProjectileKeepsItsReleaseBehaviorAfterRoundTrip(EntityType type, string format)
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

    [Theory]
    [MemberData(nameof(VanillaSavedStates))]
    public async Task VanillaBlockStateSurvivesRoundTripAndReleases(string name, EntityType type)
    {
        var probe = savedStates.EnumerateArray().Single(probe => probe.GetProperty("name").GetString() == name);
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

    private static JsonElement LoadSavedStates()
    {
        using var stream = typeof(ProjectilePersistence).Assembly
            .GetManifestResourceStream("Obsidian.Tests.Assets.sulfur_fixtures.json")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.GetProperty("cases").Clone();
    }
}
