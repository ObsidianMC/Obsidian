using Obsidian.API;
using Obsidian.API.Events;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Tests.Fakes;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public sealed class ServerPlayerTests
{
    [Fact]
    public async Task ServerPlayer_CanBeCreatedWithoutANetworkConnection()
    {
        await using var context = TestServerFactory.CreateServer($"server-player-{Guid.NewGuid():N}");

        var player = (ServerPlayer)await context.Server.AddServerPlayerAsync(Guid.NewGuid(), "ServerBot", context.World);

        Assert.IsNotType<Obsidian.Entities.Player>(player);
        Assert.IsNotAssignableFrom<IClientPlayer>(player);
        Assert.Null(player.ClientIP);
        Assert.Equal("ServerBot", player.Username);
        Assert.Same(player, context.Server.GetPlayer(player.Uuid));
    }

    [Fact]
    public async Task ServerPlayer_RegistersInOnlineLevelAndEntityState_AndTicksAndQueriesWork()
    {
        await using var context = TestServerFactory.CreateServer($"server-player-{Guid.NewGuid():N}");

        var player = (ServerPlayer)await context.Server.AddServerPlayerAsync(Guid.NewGuid(), "ServerBot", context.World);
        player.Position = context.World.LevelData.SpawnPosition;
        player.LastPosition = player.Position;

        Assert.True(context.Server.IsPlayerOnline(player.Uuid));
        Assert.True(context.World.Players.ContainsKey(player.Uuid));
        Assert.Contains(player, context.World.GetPlayersInRange(player.Position, 1f));
        Assert.Contains(player, context.World.GetEntitiesInRange(player.Position, 1f));

        await context.World.DoWorldTickAsync();

        Assert.Same(player, context.Server.GetPlayer(player.Username));
        Assert.Same(player, context.Server.GetPlayer(player.EntityId));
    }

    [Fact]
    public async Task ExistingConnectedHuman_ReceivesPlayerInfoAndSpawn_WhenServerPlayerIsAdded()
    {
        await using var context = TestServerFactory.CreateServer($"server-player-{Guid.NewGuid():N}");

        var human = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Human");
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, human);

        var client = (TestClient)human.Client;
        client.ClearPackets();

        var player = (ServerPlayer)await context.Server.AddServerPlayerAsync(Guid.NewGuid(), "ServerBot", context.World);
        player.Position = human.Position;

        Assert.True(ContainsPlayerInfo(client, player.Uuid));
        Assert.True(ContainsSpawn(client, player.EntityId));
    }

    [Fact]
    public async Task HumanJoiningAfterward_SeesExistingServerPlayer()
    {
        await using var context = TestServerFactory.CreateServer($"server-player-{Guid.NewGuid():N}");

        var player = (ServerPlayer)await context.Server.AddServerPlayerAsync(Guid.NewGuid(), "ServerBot", context.World);
        player.Position = context.World.LevelData.SpawnPosition;
        player.LastPosition = player.Position;

        var human = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "LateHuman", player.Position);
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, human);

        var client = (TestClient)human.Client;
        Assert.True(ContainsPlayerInfo(client, player.Uuid));
        Assert.True(ContainsSpawn(client, player.EntityId));
    }

    [Fact]
    public async Task RemovingServerPlayer_CleansUpObserversAndLookups()
    {
        await using var context = TestServerFactory.CreateServer($"server-player-{Guid.NewGuid():N}");

        var human = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Human");
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, human);
        var client = (TestClient)human.Client;

        var player = (ServerPlayer)await context.Server.AddServerPlayerAsync(Guid.NewGuid(), "ServerBot", context.World);
        player.Position = human.Position;
        player.LastPosition = player.Position;
        client.ClearPackets();

        Assert.True(await context.Server.RemoveServerPlayerAsync(player));

        var packetNames = string.Join(", ", client.SentPackets.Select(packet => packet.GetType().Name));
        Assert.True(ContainsPlayerInfoRemoval(client, player.Uuid), packetNames);
        Assert.True(ContainsEntityRemoval(client, player.EntityId), packetNames);
        Assert.False(context.Server.IsPlayerOnline(player.Uuid));
        Assert.False(context.World.Players.ContainsKey(player.Uuid));
        Assert.DoesNotContain(context.World.GetEntitiesInRange(player.Position, 1f), entity => entity.EntityId == player.EntityId);
    }

    [Fact]
    public async Task ConnectedPlayerJoinBehavior_RemainsIntact()
    {
        await using var context = TestServerFactory.CreateServer($"connected-player-{Guid.NewGuid():N}");

        var first = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "First");
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, first);
        var firstClient = (TestClient)first.Client;
        firstClient.ClearPackets();

        var second = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Second", first.Position);
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, second);
        var secondClient = (TestClient)second.Client;

        Assert.True(ContainsPlayerInfo(firstClient, second.Uuid));
        Assert.True(ContainsSpawn(firstClient, second.EntityId));
        Assert.True(ContainsPlayerInfo(secondClient, first.Uuid));
        Assert.True(ContainsSpawn(secondClient, first.EntityId));
    }

    [Fact]
    public async Task FailedServerPlayerCreation_RollsBackRegistrations()
    {
        await using var context = TestServerFactory.CreateServer($"server-player-{Guid.NewGuid():N}");

        context.EventDispatcher.RegisterEvent(null, (Func<PlayerJoinEventArgs, ValueTask>)(_ => throw new InvalidOperationException("boom")), Priority.Critical);

        var uuid = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.Server.AddServerPlayerAsync(uuid, "BrokenBot", context.World));

        Assert.False(context.Server.IsPlayerOnline(uuid));
        Assert.False(context.World.Players.ContainsKey(uuid));
        Assert.Null(context.Server.GetPlayer(uuid));
    }

    private static bool ContainsPlayerInfo(TestClient client, Guid uuid) =>
        client.SentPackets.OfType<PlayerInfoUpdatePacket>().Any(packet => packet.Players.ContainsKey(uuid));

    private static bool ContainsPlayerInfoRemoval(TestClient client, Guid uuid) =>
        client.SentPackets.OfType<PlayerInfoRemovePacket>().Any(packet => packet.UUIDs.Contains(uuid));

    private static bool ContainsSpawn(TestClient client, int entityId) =>
        client.SentPackets.Any(packet => packet is AddEntityPacket add && add.EntityId == entityId
            || packet is BundledPacket bundle && bundle.Packets.OfType<AddEntityPacket>().Any(add => add.EntityId == entityId));

    private static bool ContainsEntityRemoval(TestClient client, int entityId) =>
        client.SentPackets.OfType<RemoveEntitiesPacket>().Any(packet => packet.Entities.Contains(entityId));
}
