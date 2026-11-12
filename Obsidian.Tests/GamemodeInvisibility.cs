using System;
using System.Threading.Tasks;
using Obsidian.API;
using Obsidian.Tests.Fakes;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Vanilla's <c>ServerPlayer.updateInvisibilityStatus</c>, run when the game mode changes: a spectator is invisible,
/// and switching back shows the player again unless the invisibility effect still holds.
/// </summary>
public class GamemodeInvisibility
{
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

        InvisibilityEffects.AssertFlagPacket(client, player, invisible: true);

        client.ClearPackets();
        player.GameMode = GameMode.Survival;

        InvisibilityEffects.AssertFlagPacket(client, player, invisible: false);
    }

}
