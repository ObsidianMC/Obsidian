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

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task AJoinThatStartsAsTheConnectionIsDisposedAdmitsNothing()
    {
        await using var context = TestServerFactory.CreateServer($"join-races-{Guid.NewGuid():N}");
        var server = context.Server;
        var watching = await JoinWatcherAsync(context);
        var joining = CreateConfiguringPlayer(context, "Alice");
        var client = (Client)joining.Client;

        var joinEvents = 0;
        context.EventDispatcher.RegisterEvent(null, (Func<PlayerJoinEventArgs, ValueTask>)(args =>
        {
            if (args.Player.Uuid == joining.Uuid)
                Interlocked.Increment(ref joinEvents);

            return ValueTask.CompletedTask;
        }));

        // The join runs to its end once the connection is disposed, before the disconnect goes on.
        client.Disconnected += _ =>
        {
            var join = Task.Run(() => new FinishConfigurationPacket().HandleAsync(server, joining).AsTask());
            join.GetAwaiter().GetResult();
        };

        client.Disconnect();
        await server.WaitForLeavesAsync("Alice");

        Assert.Equal(0, Volatile.Read(ref joinEvents));
        Assert.False(server.IsPlayerOnline(joining.Uuid));
        AssertOutOfTheLevel(context, joining);
        Assert.False(WasEverListedFor(watching, joining.Uuid));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task AJoinHandlerThatWaitsForTheShutdownDoesNotHoldItUp()
    {
        await using var context = TestServerFactory.CreateServer($"join-races-{Guid.NewGuid():N}");
        var server = context.Server;
        var joining = CreateConfiguringPlayer(context, "Alice");

        // As the server stops, her connection closes, and a plugin's join handler waits for the stop. The stop waits
        // for her leave, which waits for her join to end.
        context.EventDispatcher.RegisterEvent(null, (Func<PlayerJoinEventArgs, ValueTask>)(async args =>
        {
            if (args.Player.Uuid != joining.Uuid)
                return;

            ((Client)joining.Client).Disconnect();
            await server.StopAsync();
        }));

        await new FinishConfigurationPacket().HandleAsync(server, joining);
        await server.StopAsync();

        AssertOutOfTheLevel(context, joining);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task AServerPlayersLeaveHandlerCannotWaitToReplaceThemUnderTheirName()
    {
        await using var context = TestServerFactory.CreateServer($"join-races-{Guid.NewGuid():N}");
        var server = context.Server;
        var uuid = Guid.NewGuid();
        var departing = await server.AddServerPlayerAsync(uuid, "Alice", context.World);

        // Her leave handler adds a new player under her name. Its join would wait for the leave running the handler.
        var attempt = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.EventDispatcher.RegisterEvent(null, (Func<PlayerLeaveEventArgs, ValueTask>)(async args =>
        {
            if (args.Player.Uuid != uuid)
                return;

            var failure = await Record.ExceptionAsync(
                () => server.AddServerPlayerAsync(Guid.NewGuid(), "Alice", context.World));
            attempt.SetResult(failure);
        }));

        Assert.True(await server.RemoveServerPlayerAsync(departing));
        Assert.IsType<InvalidOperationException>(await attempt.Task);

        // Once she left, the name is free.
        var replacement = await server.AddServerPlayerAsync(Guid.NewGuid(), "Alice", context.World);
        Assert.Same(replacement, server.GetPlayer("Alice"));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ANetworkPlayersLeaveHandlerCannotWaitToReplaceThemUnderTheirName()
    {
        await using var context = TestServerFactory.CreateServer($"join-races-{Guid.NewGuid():N}");
        var server = context.Server;
        var departing = CreateConfiguringPlayer(context, "Alice");
        await new FinishConfigurationPacket().HandleAsync(server, departing);

        // Her leave handler adds a server-side player under her name. Its join would wait for the leave running the
        // handler.
        var attempt = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.EventDispatcher.RegisterEvent(null, (Func<PlayerLeaveEventArgs, ValueTask>)(async args =>
        {
            if (args.Player.Uuid != departing.Uuid)
                return;

            var failure = await Record.ExceptionAsync(
                () => server.AddServerPlayerAsync(Guid.NewGuid(), "Alice", context.World));
            attempt.SetResult(failure);
        }));

        ((Client)departing.Client).Disconnect();
        await server.WaitForLeavesAsync("Alice");
        Assert.IsType<InvalidOperationException>(await attempt.Task);

        // Once she left, the name is free.
        var replacement = await server.AddServerPlayerAsync(Guid.NewGuid(), "Alice", context.World);
        Assert.Same(replacement, server.GetPlayer("Alice"));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ANetworkPlayerJoiningUnderANameBeingFreedWaitsForItsLeave()
    {
        await using var context = TestServerFactory.CreateServer($"join-races-{Guid.NewGuid():N}");
        var server = context.Server;
        var departing = await server.AddServerPlayerAsync(Guid.NewGuid(), "Alice", context.World);
        var joining = CreateConfiguringPlayer(context, "Alice");

        // Holds the server-side Alice's leave in its event.
        var finishLeave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.EventDispatcher.RegisterEvent(null, (Func<PlayerLeaveEventArgs, ValueTask>)(async args =>
        {
            if (args.Player.Uuid == departing.Uuid)
                await finishLeave.Task;
        }));

        // A plugin removes her once the network Alice has loaded, before she registers. The removal frees the name and
        // runs until its leave is held, all on this flow (loading a player with nothing saved doesn't wait), which then
        // goes on to the network Alice's registration.
        Task<bool>? removal = null;
        var loads = 0;
        server.PlayerLoadedForJoin = player =>
        {
            if (player.Uuid != joining.Uuid)
                return;

            loads++;
            if (loads == 1)
                removal = server.RemoveServerPlayerAsync(departing);
        };

        var registeredWhileLeaving = false;
        server.PlayerRegistered = player =>
        {
            if (player.Uuid == joining.Uuid)
                registeredWhileLeaving = !server.WaitForLeavesAsync("Alice").IsCompleted;
        };

        var join = new FinishConfigurationPacket().HandleAsync(server, joining).AsTask();
        Assert.NotNull(removal);

        finishLeave.SetResult();
        Assert.True(await removal);
        await join;

        // The second load went into a fresh player (see ANetworkJoinThatLoadsAgainLoadsLikeAFreshPlayer).
        Assert.False(registeredWhileLeaving);
        Assert.Equal(2, loads);
        Assert.Same(joining.Client.Player, server.GetPlayer("Alice"));
        Assert.Equal(joining.Uuid, server.GetPlayer("Alice")!.Uuid);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task AJoinHandlerCannotWaitToReplaceThePlayerItKickedUnderTheirName()
    {
        await using var context = TestServerFactory.CreateServer($"join-races-{Guid.NewGuid():N}");
        var server = context.Server;
        var joining = CreateConfiguringPlayer(context, "Alice");

        // A plugin turns her away from its join handler and adds a server-side player under her name. Her leave starts
        // only once her join ends, so its join would wait for the handler.
        var attempt = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.EventDispatcher.RegisterEvent(null, (Func<PlayerJoinEventArgs, ValueTask>)(async args =>
        {
            if (args.Player.Uuid != joining.Uuid)
                return;

            await args.Player.KickAsync("Replaced");
            var failure = await Record.ExceptionAsync(
                () => server.AddServerPlayerAsync(Guid.NewGuid(), "Alice", context.World));
            attempt.SetResult(failure);
        }));

        await new FinishConfigurationPacket().HandleAsync(server, joining);
        await server.WaitForLeavesAsync("Alice");

        Assert.IsType<InvalidOperationException>(await attempt.Task);
        AssertOutOfTheLevel(context, joining);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ATaskAnEndedServerPlayerLeaveStartedWaitsForTheNextLeave()
    {
        await using var context = TestServerFactory.CreateServer($"join-races-{Guid.NewGuid():N}");
        var server = context.Server;
        var first = await server.AddServerPlayerAsync(Guid.NewGuid(), "Alice", context.World);

        // Her leave handler starts a task it doesn't await, which runs on in the handler's context.
        var handlerContext = CaptureLeaveHandlerContextAsync(context, first.Uuid);
        Assert.True(await server.RemoveServerPlayerAsync(first));

        await AssertAStaleTaskWaitsForTheNextLeaveAsync(context, (await handlerContext)!);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ATaskAnEndedNetworkLeaveStartedWaitsForTheNextLeave()
    {
        await using var context = TestServerFactory.CreateServer($"join-races-{Guid.NewGuid():N}");
        var server = context.Server;
        var first = CreateConfiguringPlayer(context, "Alice");
        await new FinishConfigurationPacket().HandleAsync(server, first);

        // Her leave handler starts a task it doesn't await, which runs on in the handler's context.
        var handlerContext = CaptureLeaveHandlerContextAsync(context, first.Uuid);
        ((Client)first.Client).Disconnect();
        await server.WaitForLeavesAsync("Alice");

        await AssertAStaleTaskWaitsForTheNextLeaveAsync(context, (await handlerContext)!);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ATaskAnEndedJoinStartedWaitsForTheNextJoinsKick()
    {
        await using var context = TestServerFactory.CreateServer($"join-races-{Guid.NewGuid():N}");
        var server = context.Server;
        var joining = CreateConfiguringPlayer(context, "Alice");
        var client = (Client)joining.Client;

        // A handler of her join starts a task it doesn't await, which runs on in the join's context.
        ExecutionContext? joinContext = null;
        Assert.True(await client.AdmitAsync(() =>
        {
            joinContext = ExecutionContext.Capture();
            return ValueTask.CompletedTask;
        }));

        // The connection joins again, as after reconfiguration, and a handler of that join kicks her. Her leave waits
        // for that join, but the old task isn't part of it, so the task waits for the leave.
        Task<IPlayer>? adding = null;
        Assert.True(await client.AdmitAsync(async () =>
        {
            await client.DisconnectAsync("Kicked");
            ExecutionContext.Run(
                joinContext!,
                _ => adding = server.AddServerPlayerAsync(Guid.NewGuid(), "Alice", context.World),
                null);
        }));

        Assert.False(adding!.IsFaulted);
        await server.WaitForLeavesAsync("Alice");
        var added = await adding;
        Assert.Same(added, server.GetPlayer("Alice"));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ANetworkJoinThatLoadsAgainLoadsLikeAFreshPlayer()
    {
        await using var context = TestServerFactory.CreateServer($"join-races-{Guid.NewGuid():N}");
        var server = context.Server;
        var uuid = Guid.NewGuid();

        // Alice was saved on the ground, with a custom name.
        await SaveAliceAsync(context, uuid, player =>
        {
            player.CustomName = ChatMessage.Simple("Old");
            player.MovementFlags |= MovementFlags.OnGround;
        });

        // A server-side player under her name leaves while she loads, and her data is saved again meanwhile, in the
        // air and without the name.
        var departing = await server.AddServerPlayerAsync(Guid.NewGuid(), "Alice", context.World);
        var savedAgain = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishLeave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.EventDispatcher.RegisterEvent(null, (Func<PlayerLeaveEventArgs, ValueTask>)(async args =>
        {
            if (args.Player.Uuid != departing.Uuid)
                return;

            await SaveAliceAsync(context, uuid, player =>
            {
                player.CustomName = null;
                player.MovementFlags &= ~MovementFlags.OnGround;
            });
            savedAgain.SetResult();
            await finishLeave.Task;
        }));

        var joining = CreateConfiguringPlayer(context, "Alice", uuid);
        Task<bool>? removal = null;
        server.PlayerLoadedForJoin = player =>
        {
            if (ReferenceEquals(player, joining))
                removal = server.RemoveServerPlayerAsync(departing);
        };

        // Her join loads her, finds the name leaving, waits for that leave and loads her again.
        var join = new FinishConfigurationPacket().HandleAsync(server, joining).AsTask();
        await savedAgain.Task;
        finishLeave.SetResult();
        Assert.True(await removal!);
        await join;

        var joined = Assert.IsType<Entities.Player>(server.GetPlayer("Alice"));
        var fresh = CreateConfiguringPlayer(context, "Alice", uuid);
        await fresh.LoadAsync();

        Assert.Null(fresh.CustomName);
        Assert.Equal(fresh.CustomName, joined.CustomName);
        Assert.Equal(
            fresh.MovementFlags.HasFlag(MovementFlags.OnGround),
            joined.MovementFlags.HasFlag(MovementFlags.OnGround));
        Assert.Equal(fresh.Position, joined.Position);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task AServerPlayerJoiningUnderANameBeingFreedWaitsForItsLeave()
    {
        await using var context = TestServerFactory.CreateServer($"join-races-{Guid.NewGuid():N}");
        var server = context.Server;
        var departing = await server.AddServerPlayerAsync(Guid.NewGuid(), "Alice", context.World);
        var newUuid = Guid.NewGuid();

        // Holds the old Alice's leave in its event.
        var finishLeave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.EventDispatcher.RegisterEvent(null, (Func<PlayerLeaveEventArgs, ValueTask>)(async args =>
        {
            if (args.Player.Uuid == departing.Uuid)
                await finishLeave.Task;
        }));

        // She's removed once the new Alice has loaded, before the new one registers. The removal frees the name and
        // runs until its leave is held, all on this flow, which then goes on to the new Alice's registration.
        Task<bool>? removal = null;
        var loads = 0;
        server.PlayerLoadedForJoin = player =>
        {
            if (player.Uuid != newUuid)
                return;

            loads++;
            if (loads == 1)
                removal = server.RemoveServerPlayerAsync(departing);
        };

        var registeredWhileLeaving = false;
        server.PlayerRegistered = player =>
        {
            if (player.Uuid == newUuid)
                registeredWhileLeaving = !server.WaitForLeavesAsync("Alice").IsCompleted;
        };

        var adding = server.AddServerPlayerAsync(newUuid, "Alice", context.World);
        Assert.NotNull(removal);

        finishLeave.SetResult();
        Assert.True(await removal);
        var added = await adding;

        Assert.False(registeredWhileLeaving);
        Assert.Equal(2, loads);
        Assert.Same(added, server.GetPlayer("Alice"));
    }

    // A player whose client finished logging in and is configuring, as the join finds them.
    private static Entities.Player CreateConfiguringPlayer(
        RealServerContext context, string username, Guid? uuid = null)
    {
        var client = new Client(
            context.EventDispatcher, context.Server, NullLoggerFactory.Instance,
            new FakeUserCache(), null!, new HostServices(context), null!);
        client.SetState(ClientState.Configuration);

        var spawn = context.World.LevelData.SpawnPosition;
        var player = new Entities.Player(uuid ?? Guid.NewGuid(), username, client, context.World)
        {
            Server = context.Server,
            Position = spawn,
            LastPosition = spawn
        };
        typeof(Client).GetProperty(nameof(Client.Player))!.SetValue(client, player);
        return player;
    }

    // The test server's services, with the server itself as the host registers it, for the players a client creates.
    private sealed class HostServices(RealServerContext context) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(IServer) ? context.Server : context.Provider.GetService(serviceType);
    }

    // The context a leave handler of the player runs in, as a task the handler starts without awaiting would carry it.
    private static Task<ExecutionContext?> CaptureLeaveHandlerContextAsync(RealServerContext context, Guid uuid)
    {
        var captured = new TaskCompletionSource<ExecutionContext?>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.EventDispatcher.RegisterEvent(null, (Func<PlayerLeaveEventArgs, ValueTask>)(args =>
        {
            if (args.Player.Uuid == uuid)
                captured.TrySetResult(ExecutionContext.Capture());

            return ValueTask.CompletedTask;
        }));

        return captured.Task;
    }

    // Another Alice joins and starts leaving, held in her leave event. The stale task then adds Alice: it isn't part of
    // that leave, so it waits for it rather than failing, and joins once the leave is over.
    private static async Task AssertAStaleTaskWaitsForTheNextLeaveAsync(
        RealServerContext context, ExecutionContext staleContext)
    {
        var server = context.Server;
        var second = await server.AddServerPlayerAsync(Guid.NewGuid(), "Alice", context.World);
        var finishLeave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.EventDispatcher.RegisterEvent(null, (Func<PlayerLeaveEventArgs, ValueTask>)(async args =>
        {
            if (args.Player.Uuid == second.Uuid)
                await finishLeave.Task;
        }));

        var removal = server.RemoveServerPlayerAsync(second);

        Task<IPlayer>? adding = null;
        ExecutionContext.Run(
            staleContext,
            _ => adding = server.AddServerPlayerAsync(Guid.NewGuid(), "Alice", context.World),
            null);
        var failedAtOnce = adding!.IsFaulted;

        finishLeave.SetResult();
        Assert.True(await removal);
        Assert.False(failedAtOnce);
        var added = await adding;
        Assert.Same(added, server.GetPlayer("Alice"));
    }

    // Saves Alice's data as changed by change, as her connection's leave would.
    private static async Task SaveAliceAsync(RealServerContext context, Guid uuid, Action<Entities.Player> change)
    {
        var player = CreateConfiguringPlayer(context, "Alice", uuid);
        await player.LoadAsync();
        change(player);
        await player.SaveAsync();
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

    // Whether the client was ever sent the player's tab list entry.
    private static bool WasEverListedFor(TestClient client, Guid uuid) =>
        client.SentPackets
            .OfType<PlayerInfoUpdatePacket>()
            .Any(update => update.Players.TryGetValue(uuid, out var actions)
                && actions.OfType<AddPlayerInfoAction>().Any());

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
