using System;
using System.Threading.Tasks;
using Obsidian.API;
using Obsidian.API.Effects;
using Obsidian.Entities;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Vanilla's <c>ArmorStand.updateInvisibilityStatus</c>: a stand's invisible flag is its own, saved and set by its
/// owner, so the invisibility effect neither hides a visible stand nor shows an invisible one.
/// </summary>
public class ArmorStandInvisibility
{
    private const int InvisibilityId = (int)PotionEffect.Invisibility - 1;

    private static ArmorStand SpawnStand(World world)
    {
        var stand = new ArmorStand
        {
            Level = world,
            EntityId = Obsidian.Server.GetNextEntityId(),
            Position = new VectorD(8, 64, 8),
            Type = EntityType.ArmorStand
        };
        Assert.True(world.TryAddEntity(stand));
        return stand;
    }

    [Fact]
    public async Task InvisibleStandStaysInvisibleWhenInvisibilityIsRemoved()
    {
        await using var context = TestServerFactory.CreateServer($"stand-invisible-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(context.World);
        var stand = SpawnStand(context.World);
        stand.Invisible = true;

        stand.AddPotionEffect(InvisibilityId, 200);
        stand.RemovePotionEffect(InvisibilityId);

        Assert.False(stand.HasPotionEffect(InvisibilityId));
        Assert.True(stand.Invisible);
    }

    [Fact]
    public async Task VisibleStandStaysVisibleWithInvisibilityEffect()
    {
        await using var context = TestServerFactory.CreateServer($"stand-visible-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(context.World);
        var stand = SpawnStand(context.World);

        stand.AddPotionEffect(InvisibilityId, 200);

        Assert.True(stand.HasPotionEffect(InvisibilityId));
        Assert.False(stand.Invisible);
    }
}
