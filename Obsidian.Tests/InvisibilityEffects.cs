using System;
using System.Threading.Tasks;
using Obsidian.API;
using Obsidian.API.Effects;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Vanilla's <c>LivingEntity.updateInvisibilityStatus</c>: the invisible flag follows the invisibility effect, and a
/// spectator player stays invisible whatever its effects are.
/// </summary>
public class InvisibilityEffects
{
    private const int InvisibilityId = (int)PotionEffect.Invisibility - 1;

    private static Pig SpawnPig(World world)
    {
        var pig = new Pig
        {
            Level = world,
            EntityId = Obsidian.Server.GetNextEntityId(),
            Position = new VectorD(8, 64, 8),
            Type = EntityType.Pig
        };
        Assert.True(world.TryAddEntity(pig));
        return pig;
    }

    [Fact]
    public async Task AddingInvisibilitySetsTheInvisibleFlag()
    {
        await using var context = TestServerFactory.CreateServer($"invisibility-add-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(context.World);
        var pig = SpawnPig(context.World);
        Assert.False(pig.Invisible);

        pig.AddPotionEffect(InvisibilityId, 200);

        Assert.True(pig.Invisible);
    }

    [Fact]
    public async Task RemovingInvisibilityClearsTheInvisibleFlag()
    {
        await using var context = TestServerFactory.CreateServer($"invisibility-remove-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(context.World);
        var pig = SpawnPig(context.World);
        pig.AddPotionEffect(InvisibilityId, 200);
        Assert.True(pig.Invisible);

        pig.RemovePotionEffect(InvisibilityId);

        Assert.False(pig.HasPotionEffect(InvisibilityId));
        Assert.False(pig.Invisible);
    }

    [Fact]
    public async Task ExpiringInvisibilityClearsTheInvisibleFlag()
    {
        await using var context = TestServerFactory.CreateServer($"invisibility-expire-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(context.World);
        var pig = SpawnPig(context.World);
        pig.AddPotionEffect(InvisibilityId, 2);

        // The effect runs out on the second tick.
        await pig.TickAsync();
        Assert.True(pig.Invisible);
        await pig.TickAsync();

        Assert.False(pig.HasPotionEffect(InvisibilityId));
        Assert.False(pig.Invisible);
    }

    [Fact(DisplayName = "A mob's invisibility change is sent to the players watching it", Timeout = TestServerFactory.TestTimeout)]
    public async Task InvisibilityChangesAreSentToWatchers()
    {
        await using var context = TestServerFactory.CreateServer($"invisibility-watchers-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(context.World);
        var position = new VectorD(8, 64, 8);
        var viewer = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "InvisibilityWatcher", position);
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, viewer);
        viewer.LoadedChunks.Add(0L);
        var pig = Assert.IsType<Pig>(context.World.SpawnEntity(position, EntityType.Pig));
        var client = (TestClient)viewer.Client;
        client.ClearPackets();

        pig.AddPotionEffect(InvisibilityId, 200);

        Assert.Contains(client.SentPackets, packet => packet is SetEntityDataPacket data && data.EntityId == pig.EntityId);

        client.ClearPackets();
        pig.RemovePotionEffect(InvisibilityId);

        Assert.Contains(client.SentPackets, packet => packet is SetEntityDataPacket data && data.EntityId == pig.EntityId);
    }

    [Fact]
    public async Task SpectatorStaysInvisibleWhenInvisibilityIsRemoved()
    {
        await using var context = TestServerFactory.CreateServer($"invisibility-spectator-{Guid.NewGuid():N}");
        var player = new ServerPlayer(Guid.NewGuid(), "SpectatorFixture", null!, context.World)
        {
            GameMode = GameMode.Spectator
        };

        player.AddPotionEffect(InvisibilityId, 200);
        Assert.True(player.Invisible);

        player.RemovePotionEffect(InvisibilityId);

        Assert.True(player.Invisible);
    }
}
