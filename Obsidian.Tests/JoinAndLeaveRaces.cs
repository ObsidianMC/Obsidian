using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.Events;
using Obsidian.Net.Actions.PlayerInfo;
using Obsidian.Net.Packets.Configuration.Serverbound;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Tests.Fakes;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// That a join and the end of the player's connection, or a server-side player's removal, never leave a player behind:
/// a disconnect either keeps the join from admitting anything or undoes all of it, and a removal leaves nothing that
/// finds another player under the removed one's name.
/// </summary>
public sealed class JoinAndLeaveRaces
{
    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task AClientThatDisconnectsBeforeTheJoinAdmitsThePlayerIsNeverAdmitted()
    {
        await using var context = TestServerFactory.CreateServer($"join-races-{Guid.NewGuid():N}");
        var server = context.Server;
        var watching = await JoinWatcherAsync(context);
        var joining = CreateConfiguringPlayer(context, "Alice");

        // Holds the join once it registered Alice, before it admits her to her level.
        var registered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        server.PlayerRegistered = player =>
        {
            if (!ReferenceEquals(player, joining))
                return;

            registered.SetResult();
            release.Wait();
        };

        var join = Task.Run(() => new FinishConfigurationPacket().HandleAsync(server, joining).AsTask());
        await registered.Task;

        // Her client disconnects there, and any leave that starts is over before the join goes on.
        ((Client)joining.Client).Disconnect();
        await server.WaitForLeavesAsync("Alice");
        release.Set();
        await join;

        Assert.False(server.IsPlayerOnline(joining.Uuid));
        Assert.False(server.IsPlayerOnline("Alice"));
        AssertOutOfTheLevel(context, joining);
        Assert.False(IsListedFor(watching, joining.Uuid));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task APlayerKickedWhileJoiningLeavesOnceTheJoinIsOver()
    {
        await using var context = TestServerFactory.CreateServer($"join-races-{Guid.NewGuid():N}");
        var server = context.Server;
        var watching = await JoinWatcherAsync(context);
        var joining = CreateConfiguringPlayer(context, "Alice");

        // A plugin turns her away from its join handler, which runs before the server admits her to her level.
        context.EventDispatcher.RegisterEvent(null, (Func<PlayerJoinEventArgs, ValueTask>)(async args =>
        {
            if (args.Player.Uuid == joining.Uuid)
                await args.Player.KickAsync("Turned away");
        }));

        await new FinishConfigurationPacket().HandleAsync(server, joining);
        await server.WaitForLeavesAsync("Alice");

        AssertOutOfTheLevel(context, joining);
        Assert.False(IsListedFor(watching, joining.Uuid));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task AServerPlayersNameIsFreedWithItsRegistrationNotAfterItsLeave()
    {
        await using var context = TestServerFactory.CreateServer($"join-races-{Guid.NewGuid():N}");
        var server = context.Server;
        var uuid = Guid.NewGuid();
        var removed = await server.AddServerPlayerAsync(uuid, "Alice", context.World);

        // Holds her leave in its event.
        var leaveRaised = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishLeave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.EventDispatcher.RegisterEvent(null, (Func<PlayerLeaveEventArgs, ValueTask>)(async args =>
        {
            if (args.Player.Uuid != uuid)
                return;

            leaveRaised.TrySetResult();
            await finishLeave.Task;
        }));

        var removal = server.RemoveServerPlayerAsync(removed);
        await leaveRaised.Task;

        // The same UUID joins under another name before her leave is over. Nothing finds it under her name.
        var renamed = TestServerFactory.CreateConnectedPlayer(server, context.World, "Alicia", uuid: uuid);
        Assert.True(server.AddPlayer(renamed));
        Assert.Null(server.GetPlayer("Alice"));
        Assert.False(server.IsPlayerOnline("Alice"));

        // Her leave ending takes nothing from the new registration.
        finishLeave.SetResult();
        Assert.True(await removal);

        Assert.Null(server.GetPlayer("Alice"));
        Assert.Same(renamed, server.GetPlayer("Alicia"));
        Assert.Equal<IPlayer>([renamed], server.PlayersInListOrder);
    }

    // A player whose client finished logging in and is configuring, as the join finds them.
    private static Entities.Player CreateConfiguringPlayer(RealServerContext context, string username)
    {
        var client = new Client(
            context.EventDispatcher, context.Server, NullLoggerFactory.Instance,
            new FakeUserCache(), null!, context.Provider, null!);
        client.SetState(ClientState.Configuration);

        var spawn = context.World.LevelData.SpawnPosition;
        var player = new Entities.Player(Guid.NewGuid(), username, client, context.World)
        {
            Server = context.Server,
            Position = spawn,
            LastPosition = spawn
        };
        typeof(Client).GetProperty(nameof(Client.Player))!.SetValue(client, player);
        return player;
    }

    // A player already online, who sees the others' tab list entries.
    private static async Task<TestClient> JoinWatcherAsync(RealServerContext context)
    {
        var watcher = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Watcher");
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, watcher);
        return (TestClient)watcher.Client;
    }

    private static void AssertOutOfTheLevel(RealServerContext context, Entities.Player player)
    {
        Assert.False(context.World.Players.ContainsKey(player.Uuid));

        var nearby = context.World.GetEntitiesInRange(player.Position, 1f);
        Assert.DoesNotContain(nearby, entity => entity.EntityId == player.EntityId);
    }

    // Whether the last tab list change the client was sent for the player adds them.
    private static bool IsListedFor(TestClient client, Guid uuid)
    {
        var listed = false;
        foreach (var packet in client.SentPackets)
        {
            if (packet is PlayerInfoUpdatePacket update
                && update.Players.TryGetValue(uuid, out var actions)
                && actions.OfType<AddPlayerInfoAction>().Any())
            {
                listed = true;
            }
            else if (packet is PlayerInfoRemovePacket removal && removal.UUIDs.Contains(uuid))
            {
                listed = false;
            }
        }

        return listed;
    }
}
