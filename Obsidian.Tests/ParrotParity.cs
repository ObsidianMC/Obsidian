using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Tests.Fakes;
using System;
using Xunit;

namespace Obsidian.Tests;

public sealed class ParrotParity
{
    [Fact]
    public void ImitationMapIncludesAllVanillaTargetsAndSoundAliases()
    {
        Assert.Equal(42, Parrot.ImitationSounds.Count);
        Assert.Equal("spider", Parrot.ImitationSounds[EntityType.CaveSpider]);
        Assert.Equal("", Parrot.ImitationSounds[EntityType.HappyGhast]);
        Assert.Equal("breeze", Parrot.ImitationSounds[EntityType.Breeze]);
        Assert.Equal("creaking", Parrot.ImitationSounds[EntityType.Creaking]);
        Assert.Equal("camel_husk", Parrot.ImitationSounds[EntityType.CamelHusk]);
        Assert.Equal("parched", Parrot.ImitationSounds[EntityType.Parched]);
        Assert.Equal("zombie_horse", Parrot.ImitationSounds[EntityType.ZombieHorse]);
        Assert.Equal("zombie_nautilus", Parrot.ImitationSounds[EntityType.ZombieNautilus]);
        Assert.False(Parrot.ImitationSounds.ContainsKey(EntityType.Enderman));
    }

    [Fact]
    public async System.Threading.Tasks.Task FollowingStartsAtFiveBlocksAndStopsWhenOrderedToSit()
    {
        await using var context = TestServerFactory.CreateServer($"parrot-follow-{Guid.NewGuid():N}");
        var owner = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Owner");
        Assert.True(context.World.TryAddPlayer(owner));
        owner.Position = new VectorD(5, 64, 0);
        var parrot = new Parrot { Level = context.World, Owner = owner.Uuid, Position = new VectorD(0, 64, 0) };
        var goal = new ParrotFollowGoal(parrot);
        Assert.True(goal.CanUse());
        owner.Position = new VectorD(4.99f, 64, 0);
        Assert.False(goal.CanUse());
        owner.Position = new VectorD(20, 64, 0);
        parrot.OrderedToSit = true;
        Assert.False(goal.CanUse());
    }
}
