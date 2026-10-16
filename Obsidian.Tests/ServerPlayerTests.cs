using Obsidian.API;
using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.Entities;
using Obsidian.Net.Actions.PlayerInfo;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Tests.Fakes;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public sealed class ServerPlayerTests
{
    [Fact]
    public async Task ServerPlayerLifecycleReachesConnectedHumans()
    {
        await using var context = TestServerFactory.CreateServer($"server-player-{Guid.NewGuid():N}");

        var human = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Human");
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, human);

        var client = (TestClient)human.Client;
        client.ClearPackets();

        var player = (ServerPlayer)await context.Server.AddServerPlayerAsync(
            Guid.NewGuid(), "ServerBot", context.World);
        player.Position = human.Position;

        Assert.True(ContainsPlayerInfo(client, player.Uuid));
        Assert.True(ContainsListedFlag(client, player.Uuid, listed: true));
        Assert.True(ContainsSpawn(client, player.EntityId));

        Assert.IsNotAssignableFrom<IClientPlayer>(player);
        Assert.Null(player.ClientIP);
        Assert.Same(player, context.Server.GetPlayer(player.Uuid));
        Assert.Same(player, context.Server.GetPlayer(player.Username));
        Assert.Same(player, context.World.Players[player.Uuid]);
        Assert.Contains(player, context.World.GetEntitiesInRange(player.Position, 1));
        await context.World.DoWorldTickAsync();

        var late = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "LateHuman", player.Position);
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, late);
        var lateClient = (TestClient)late.Client;
        Assert.True(ContainsPlayerInfo(lateClient, player.Uuid));
        Assert.True(ContainsSpawn(lateClient, player.EntityId));

        client.ClearPackets();
        await player.SetGamemodeAsync(GameMode.Creative);
        Assert.Contains(client.SentPackets.OfType<PlayerInfoUpdatePacket>(), packet =>
            packet.Players.TryGetValue(player.Uuid, out var actions)
            && actions.OfType<UpdateGamemodeInfoAction>().Any());

        // Gameplay takes server-side players too: what they drop counts in their statistics, and creative adds to
        // their reach.
        Assert.True(ItemEntity.Drop(player, new ItemStack(ItemsRegistry.Get("minecraft:stone"), 3)));
        Assert.Equal(3, player.Statistics.Get("minecraft:stone", "minecraft:dropped"));
        Assert.Equal(5, Entities.Player.BlockInteractionRange(player));

        await ((Obsidian.WorldData.AbstractLevel)context.World).ExplodeAsync(player, 1f);
        Assert.Contains(client.SentPackets, packet => packet is ExplodePacket);

        client.ClearPackets();
        Assert.True(await context.Server.RemoveServerPlayerAsync(player));
        Assert.True(ContainsPlayerInfoRemoval(client, player.Uuid));
        Assert.True(ContainsEntityRemoval(client, player.EntityId));
        Assert.False(context.Server.IsPlayerOnline(player.Uuid));
        Assert.False(context.World.Players.ContainsKey(player.Uuid));
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
    public async Task FailedAndStaleServerPlayerOperationsPreserveRegistrations()
    {
        {
            await using var context = TestServerFactory.CreateServer($"server-player-{Guid.NewGuid():N}");

            var existing = await context.Server.AddServerPlayerAsync(Guid.NewGuid(), "ServerBot", context.World);

            // Same UUID under a new name, then the same name under a new UUID.
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => context.Server.AddServerPlayerAsync(existing.Uuid, "OtherBot", context.World));
            var otherUuid = Guid.NewGuid();
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => context.Server.AddServerPlayerAsync(otherUuid, "ServerBot", context.World));

            Assert.False(context.Server.IsPlayerOnline("OtherBot"));
            Assert.False(context.Server.IsPlayerOnline(otherUuid));
            Assert.Same(existing, context.Server.GetPlayer(existing.Uuid));
            Assert.Same(existing, context.Server.GetPlayer("ServerBot"));
        }

        {
            await using var context = TestServerFactory.CreateServer($"server-player-{Guid.NewGuid():N}");

            context.EventDispatcher.RegisterEvent(
                null,
                (Func<PlayerJoinEventArgs, ValueTask>)(_ => throw new InvalidOperationException("boom")),
                Priority.Critical);

            var uuid = Guid.NewGuid();
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => context.Server.AddServerPlayerAsync(uuid, "BrokenBot", context.World));

            Assert.False(context.Server.IsPlayerOnline(uuid));
            Assert.False(context.World.Players.ContainsKey(uuid));
            Assert.Null(context.Server.GetPlayer(uuid));
        }

        {
            await using var context = TestServerFactory.CreateServer($"server-player-{Guid.NewGuid():N}");

            var uuid = Guid.NewGuid();
            var stale = await context.Server.AddServerPlayerAsync(uuid, "ServerBot", context.World);
            Assert.True(await context.Server.RemoveServerPlayerAsync(stale));
            var replacement = await context.Server.AddServerPlayerAsync(uuid, "ServerBot", context.World);

            Assert.False(await context.Server.RemoveServerPlayerAsync(stale));
            Assert.False(context.Server.RemovePlayer(stale));

            Assert.Same(replacement, context.Server.GetPlayer(uuid));
            Assert.Same(replacement, context.Server.GetPlayer("ServerBot"));
            Assert.Same(replacement, context.World.Players[uuid]);
        }
    }

    [Fact]
    public async Task FailingLeaveHandler_StillDespawnsServerPlayer()
    {
        await using var context = TestServerFactory.CreateServer($"server-player-{Guid.NewGuid():N}");

        var human = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Human");
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, human);
        var client = (TestClient)human.Client;

        var player = await context.Server.AddServerPlayerAsync(Guid.NewGuid(), "ServerBot", context.World);
        // Runs before the internal leave handler and stops it from running.
        context.EventDispatcher.RegisterEvent(
            null,
            (Func<PlayerLeaveEventArgs, ValueTask>)(_ => throw new InvalidOperationException("boom")),
            Priority.Critical);
        client.ClearPackets();

        Assert.True(await context.Server.RemoveServerPlayerAsync(player));

        Assert.True(ContainsPlayerInfoRemoval(client, player.Uuid));
        Assert.True(ContainsEntityRemoval(client, player.EntityId));
        Assert.False(context.Server.IsPlayerOnline(player.Uuid));
        Assert.False(context.World.Players.ContainsKey(player.Uuid));
        Assert.DoesNotContain(
            context.World.GetEntitiesInRange(player.Position, 1f), entity => entity.EntityId == player.EntityId);
    }

    private static bool ContainsPlayerInfo(TestClient client, Guid uuid) =>
        client.SentPackets.OfType<PlayerInfoUpdatePacket>().Any(packet => packet.Players.ContainsKey(uuid));

    private static bool ContainsListedFlag(TestClient client, Guid uuid, bool listed) =>
        client.SentPackets.OfType<PlayerInfoUpdatePacket>()
            .Where(packet => packet.Players.ContainsKey(uuid))
            .SelectMany(packet => packet.Players[uuid])
            .OfType<UpdateListedInfoAction>()
            .Any(action => action.Listed == listed);

    private static bool ContainsPlayerInfoRemoval(TestClient client, Guid uuid) =>
        client.SentPackets.OfType<PlayerInfoRemovePacket>().Any(packet => packet.UUIDs.Contains(uuid));

    private static bool ContainsSpawn(TestClient client, int entityId) =>
        client.SentPackets.Any(packet => packet is AddEntityPacket add && add.EntityId == entityId
            || packet is BundledPacket bundle && bundle.Packets.OfType<AddEntityPacket>(
                ).Any(add => add.EntityId == entityId));

    private static bool ContainsEntityRemoval(TestClient client, int entityId) =>
        client.SentPackets.OfType<RemoveEntitiesPacket>().Any(packet => packet.Entities.Contains(entityId));
}
