using Obsidian.API;
using Obsidian.API.Commands;
using Obsidian.Commands;
using Obsidian.Tests.Fakes;
using System.Linq;
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

    [Fact(Timeout = TestServerFactory.TestTimeout)]
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

    [Fact(Timeout = TestServerFactory.TestTimeout)]
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

    private static async Task<Obsidian.Entities.Player> JoinAsync(RealServerContext context, string name)
    {
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, name, spot);
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, player);
        return player;
    }

    private static string[] Select(RealServerContext context, string selector)
    {
        Assert.True(EntitySelector.TryParse(selector, out var parsed));
        var sender = new CommandSender(CommandIssuers.Console, null);
        var command = new CommandContext("/", sender, null, context.Server) { Origin = spot, Level = context.World };
        return [.. parsed.Select(command).OfType<IPlayer>().Select(player => player.Username)];
    }
}
