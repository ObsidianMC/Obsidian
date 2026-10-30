using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Tests.Fakes;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public sealed class ProjectilePersistence
{
    [Theory]
    [InlineData(EntityType.Arrow, false)]
    [InlineData(EntityType.SpectralArrow, false)]
    [InlineData(EntityType.Trident, false)]
    [InlineData(EntityType.Arrow, true)]
    [InlineData(EntityType.SpectralArrow, true)]
    [InlineData(EntityType.Trident, true)]
    public async Task EmbeddedProjectileKeepsItsReleaseBehaviorAfterRoundTrip(EntityType type, bool legacy)
    {
        await using var context = TestServerFactory.CreateServer($"projectile-save-{Guid.NewGuid():N}");
        var world = context.World;
        await BlockUpdates.AddChunksAsync(world);
        using var simulation = world.AddChunkTicket(0, 0);
        var target = new Vector(1, 72, 1);
        await world.SetBlockAsync(target, VanillaSulfurBlocks.ParseState("minecraft:stone"));
        Arrow original = type switch
        {
            EntityType.Trident => new Trident { Level = world },
            EntityType.SpectralArrow => new SpectralArrow { Level = world },
            _ => new Arrow { Level = world }
        };
        original.Type = type;
        original.EntityId = Obsidian.Server.GetNextEntityId();
        original.Position = new VectorD(1.5, 72.5, 0.1);
        original.Motion = new VectorD(0, 0, 2);
        original.NoGravity = true;
        await original.TickAsync();
        var saved = EntityNbt.Save(original)!;
        Assert.True(saved.TryGetTagValue<bool>("inGround", out var embedded) && embedded);
        if (legacy)
        {
            saved.Remove("ObsidianEmbeddedBlock");
        }

        var restored = Assert.IsAssignableFrom<Arrow>(EntityNbt.Load(saved, world));
        Assert.Equal(original.GetType(), restored.GetType());
        Assert.Equal(original.Position, restored.Position);

        foreach (var arrow in new[] { original, restored })
        {
            await arrow.TickAsync();
            Assert.True(EntityNbt.Save(arrow)!.TryGetTagValue<bool>("inGround", out embedded) && embedded);
        }

        await world.SetBlockAsync(target, VanillaSulfurBlocks.ParseState("minecraft:air"));
        foreach (var arrow in new[] { original, restored })
        {
            await arrow.TickAsync();
            Assert.True(EntityNbt.Save(arrow)!.TryGetTagValue<bool>("inGround", out embedded));
            Assert.False(embedded);
        }

        Assert.Equal(original.Position, restored.Position);
        Assert.Equal(original.Motion, restored.Motion);
    }
}
