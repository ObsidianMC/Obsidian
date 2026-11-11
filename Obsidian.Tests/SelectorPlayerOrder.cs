using Obsidian.API;
using Obsidian.API.Commands;
using Obsidian.Commands;
using Obsidian.Tests.Fakes;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// That player selectors go by vanilla's player list order, which Obsidian keeps itself: the online players'
/// dictionary has no order, and entity ids are handed out before the order of joining is settled.
/// </summary>
public sealed class SelectorPlayerOrder
{
    private static readonly VectorD spot = new(8.5, 64, 5.5);

    [Fact]
    public async Task PlayersComeInTheOrderTheyJoinedNotByEntityId()
    {
        await using var context = TestServerFactory.CreateServer($"selector-order-{System.Guid.NewGuid():N}");

        // Alice's connection got the lower entity id, but Bob finished joining first.
        var alice = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Alice", spot);
        var bob = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Bob", spot);
        Assert.True(alice.EntityId < bob.EntityId);
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, bob);
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, alice);

        Assert.Equal(["Bob", "Alice"], Select(context, "@a"));
        Assert.Equal(["Bob"], Select(context, "@a[limit=1]"));
    }

    [Fact]
    public async Task APlayerWhoRespawnedComesAfterThoseWhoDidNot()
    {
        await using var context = TestServerFactory.CreateServer($"selector-order-{System.Guid.NewGuid():N}");
        var alice = await JoinAsync(context, "Alice");
        var bob = await JoinAsync(context, "Bob");
        Assert.Equal(["Alice", "Bob"], Select(context, "@a"));

        // Like vanilla's PlayerList.respawn, respawning takes Alice out of the list and puts her back at its end.
        await alice.KillAsync(alice, ChatMessage.Simple("Alice died"));
        await alice.RespawnAsync();

        Assert.Equal(["Bob", "Alice"], Select(context, "@a"));
        Assert.Equal(["Bob"], Select(context, "@a[limit=1]"));

        // Among players as near as each other, @p keeps the list's order.
        await alice.TeleportAsync(spot);
        await bob.TeleportAsync(spot);
        Assert.Equal(["Bob"], Select(context, "@p"));

        // A player who leaves drops out of the order, and one who joins again comes last.
        Assert.True(context.Server.RemovePlayer(bob));
        Assert.Equal(["Alice"], Select(context, "@a"));
        Assert.True(context.Server.AddPlayer(bob));
        Assert.Equal(["Alice", "Bob"], Select(context, "@a"));
    }

    [Fact]
    public async Task APlayerWhoDisconnectsWhileJoiningLeavesTheirNextJoinRegistered()
    {
        await using var context = TestServerFactory.CreateServer($"selector-order-{System.Guid.NewGuid():N}");
        var server = context.Server;
        var first = TestServerFactory.CreateConnectedPlayer(server, context.World, "Alice", spot);

        // Holds the first join once it registered Alice.
        var registered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        server.PlayerRegistered = player =>
        {
            if (!ReferenceEquals(player, first))
                return;

            registered.SetResult();
            release.Wait();
        };

        var joining = Task.Run(() => server.AddPlayer(first));
        await registered.Task;

        // Her client disconnects there, and she joins again on a new connection before the first join is over.
        Assert.True(server.RemovePlayer(first));
        var second = TestServerFactory.CreateConnectedPlayer(server, context.World, "Alice", spot, first.Uuid);
        Assert.True(server.AddPlayer(second));

        // Whatever the first join does once it goes on, it leaves nothing of the first connection behind, and takes
        // nothing from the second.
        release.Set();
        await joining;

        Assert.Equal([second], Listed(server));
        Assert.Same(second, server.GetPlayer("Alice"));
        Assert.True(server.IsPlayerOnline("Alice"));
        Assert.Same(second, server.OnlinePlayers[first.Uuid]);
    }

    private static async Task<Obsidian.Entities.Player> JoinAsync(RealServerContext context, string name)
    {
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, name, spot);
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, player);
        return player;
    }

    // The player list itself, which keeps a reference to every player in it, online or not.
    private static IPlayer[] Listed(Server server)
    {
        var field = typeof(Server).GetField("playerList", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var list = (List<IPlayer>)field.GetValue(server)!;
        lock (list)
            return [.. list];
    }

    private static string[] Select(RealServerContext context, string selector)
    {
        Assert.True(EntitySelector.TryParse(selector, out var parsed));
        var sender = new CommandSender(CommandIssuers.Console, null);
        var command = new CommandContext("/", sender, null, context.Server) { Origin = spot, Level = context.World };
        return [.. parsed.Select(command).OfType<IPlayer>().Select(player => player.Username)];
    }
}
