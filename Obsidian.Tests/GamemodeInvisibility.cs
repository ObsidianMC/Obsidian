using System;
using System.Threading.Tasks;
using Obsidian.API;
using Obsidian.API.Effects;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Tests.Fakes;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Vanilla's <c>ServerPlayer.updateInvisibilityStatus</c>, run when the game mode changes: a spectator is invisible,
/// and switching back shows the player again unless the invisibility effect still holds.
/// </summary>
public class GamemodeInvisibility
{
    private const int InvisibilityId = (int)PotionEffect.Invisibility - 1;

    private static ServerPlayer NewPlayer(IWorld world) =>
        new(Guid.NewGuid(), "GamemodeFixture", null!, world)
        {
            GameMode = GameMode.Survival
        };

    [Fact]
    public async Task SwitchingToSpectatorMakesThePlayerInvisible()
    {
        await using var context = TestServerFactory.CreateServer($"gamemode-spectate-{Guid.NewGuid():N}");
        var player = NewPlayer(context.World);
        Assert.False(player.Invisible);

        player.GameMode = GameMode.Spectator;

        Assert.True(player.Invisible);
    }

    [Fact(DisplayName = "A player's invisibility change is sent to the player itself", Timeout = TestServerFactory.TestTimeout)]
    public async Task SpectatingIsSentToThePlayerItself()
    {
        await using var context = TestServerFactory.CreateServer($"gamemode-self-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(context.World);
        var position = new VectorD(8, 64, 8);
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "SpectatingWatcher", position);
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, player);
        player.LoadedChunks.Add(0L);
        var client = (TestClient)player.Client;
        client.ClearPackets();

        player.GameMode = GameMode.Spectator;

        Assert.Contains(client.SentPackets, packet => packet is SetEntityDataPacket data && data.EntityId == player.EntityId);
    }

    [Fact]
    public async Task SwitchingBackFromSpectatorMakesThePlayerVisible()
    {
        await using var context = TestServerFactory.CreateServer($"gamemode-visible-{Guid.NewGuid():N}");
        var player = NewPlayer(context.World);
        player.GameMode = GameMode.Spectator;
        Assert.True(player.Invisible);

        player.GameMode = GameMode.Survival;

        Assert.False(player.Invisible);
    }

    [Fact]
    public async Task SwitchingBackFromSpectatorKeepsInvisibilityFromTheEffect()
    {
        await using var context = TestServerFactory.CreateServer($"gamemode-effect-{Guid.NewGuid():N}");
        var player = NewPlayer(context.World);
        player.GameMode = GameMode.Spectator;
        player.AddPotionEffect(InvisibilityId, 200);
        Assert.True(player.Invisible);

        player.GameMode = GameMode.Survival;

        Assert.True(player.Invisible);
    }
}
