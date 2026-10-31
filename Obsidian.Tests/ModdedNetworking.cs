using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.ObjectPool;
using Obsidian.API;
using Obsidian.API.Networking;
using Obsidian.API.Plugins;
using Obsidian.Events.EventArgs;
using Obsidian.Net;
using Obsidian.Net.Packets.Common;
using Obsidian.Net.Packets.Login.Clientbound;
using Obsidian.Net.Payloads;
using Obsidian.Services;
using Obsidian.Tests.Fakes;
using Obsidian.Utilities;
using Obsidian.Utilities.Collections;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Obsidian.Tests.Utilities;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Plugin payload channels and NeoForge's configuration handshake.
/// </summary>
public class ModdedNetworking
{
    // Each side's channels: "id=version", then ">" for clientbound or "<" for serverbound, then "?" when optional.
    // The expected outcome lists the agreed channels as "id=version", or the refused ones as "!id".
    [Fact(DisplayName = "Channels are negotiated with NeoForge's rules")]
    public void NegotiatesChannels()
    {
        (string Server, string Client, string Expected)[] cases =
        [
            ("mod:a=2", "mod:a=1", "!mod:a"),
            ("mod:a=1>", "mod:a=1<", "!mod:a"),
            ("", "mod:a=1", "!mod:a"),
            ("mod:a=1 mod:b=1?", "mod:a=1 mod:c=1?", "mod:a=1")
        ];

        FixtureCases.AssertAll(
            cases,
            row => $"server={row.Server}, client={row.Client}, expected={row.Expected}",
            row => AssertNegotiatesChannels(row.Server, row.Client, row.Expected));
    }

    private static void AssertNegotiatesChannels(string server, string client, string expected)
    {
        var result = NetworkComponentNegotiator.Negotiate(ParseComponents(server), ParseComponents(client));

        var outcome = result.Success
            ? result.Channels.Select(channel => $"{channel.Key}={channel.Value}")
            : result.FailureReasons.Keys.Select(id => $"!{id}");

        Assert.Equal(expected, string.Join(' ', outcome.Order()));
    }

    [Fact(DisplayName = "A NeoForge client agrees on channels, then exchanges payloads until the plugin unloads")]
    public async Task NeoForgeHandshake()
    {
        var registry = new PayloadChannelRegistry();
        var plugin = new FakePluginContainer();
        var received = new List<string>();

        registry.Register<EchoPayload>(plugin, PayloadPhase.Common, PayloadDirection.Bidirectional,
            (payload, context) =>
            {
                received.Add(payload.Text);
                return ValueTask.CompletedTask;
            }, version: "2", optional: false);

        var client = new TestClient(new FakeServer());
        client.SetState(ClientState.Configuration);
        var channels = new ClientChannels(client, registry);

        channels.StartConfiguration();

        // Like NeoForge's server: announce the handshake channels, send an empty query, then ping.
        Assert.Equal(
            [ChannelPayloads.Unregister, ChannelPayloads.Register, ChannelPayloads.NeoForgeQuery],
            client.SentPackets.OfType<CustomPayloadPacket>().Select(packet => packet.Channel));
        Assert.Equal(
            "minecraft:register\0minecraft:unregister\0neoforge:register\0",
            Encoding.ASCII.GetString(PayloadOn(client, ChannelPayloads.Register)));
        Assert.Equal([0x00], PayloadOn(client, ChannelPayloads.NeoForgeQuery));
        Assert.Equal(0, Assert.IsType<PingPacket>(client.SentPackets[^1]).IdValue);
        client.ClearPackets();

        var echo = new NetworkComponent(EchoPayload.Channel, "2", null, false);
        var query = new Dictionary<NetworkProtocol, List<NetworkComponent>>
        {
            [NetworkProtocol.Configuration] = [echo],
            [NetworkProtocol.Play] = [echo]
        };

        var queryPayload = Write(writer => ChannelPayloads.WriteQuery(writer, query));
        await channels.HandlePayloadAsync(ChannelPayloads.NeoForgeQuery, queryPayload);

        Assert.True(channels.IsNeoForge);
        Assert.True(client.Connected);

        // Per protocol (configuration is 4, play is 1): the agreed channel's id, then the channel's id and version.
        static void WriteEchoSetup(INetStreamWriter writer, int protocol)
        {
            writer.WriteVarInt(protocol);
            writer.WriteVarInt(1);
            writer.WriteString(EchoPayload.Channel);
            writer.WriteString(EchoPayload.Channel);
            writer.WriteString("2");
        }

        var setup = Write(writer =>
        {
            writer.WriteVarInt(2);
            WriteEchoSetup(writer, 4);
            WriteEchoSetup(writer, 1);
        });
        Assert.Equal(setup, PayloadOn(client, ChannelPayloads.NeoForgeNetwork));
        Assert.Equal(
            "minecraft:register\0minecraft:unregister\0neoforge:register\0test:echo\0",
            Encoding.ASCII.GetString(PayloadOn(client, ChannelPayloads.Register)));

        Assert.True(await channels.FinishHandshakeAsync(0));
        Assert.False(await channels.FinishHandshakeAsync(0));

        await channels.HandlePayloadAsync(EchoPayload.Channel, Write(new EchoPayload("hello").Write));
        Assert.Equal(["hello"], received);

        Assert.True(await channels.SendAsync(new EchoPayload("back")));

        // Nothing handles the channel while its plugin is gone; a reload with the agreed version restores it, but one
        // with another version doesn't, since the client still speaks the old one.
        registry.UnregisterPluginPayloads(plugin);
        await channels.HandlePayloadAsync(EchoPayload.Channel, Write(new EchoPayload("dropped").Write));

        Assert.Equal(["hello"], received);
        Assert.False(channels.HasChannel(EchoPayload.Channel));

        registry.Register<EchoPayload>(plugin, PayloadPhase.Common, PayloadDirection.Bidirectional, null, "2", false);
        Assert.True(channels.HasChannel(EchoPayload.Channel));

        registry.UnregisterPluginPayloads(plugin);
        registry.Register<EchoPayload>(plugin, PayloadPhase.Common, PayloadDirection.Bidirectional, null, "3", false);
        Assert.False(channels.HasChannel(EchoPayload.Channel));

        await AssertOutgoingLifecycleAsync();
    }

    // Real loopback output proves the framing transition and terminal reason, which recording clients cannot see.
    private static async Task AssertOutgoingLifecycleAsync()
    {
        await using var context = TestServerFactory.CreateServer($"pipeline-{Guid.NewGuid():N}");
        using var metrics = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var serverMetrics = new ServerMetrics(metrics.GetRequiredService<IMeterFactory>());
        var pool = new SimpleObjectPool<SocketAsyncEventArgs>(8);
        var unsent = typeof(Client).GetField("unsent", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var consumer = typeof(Client).GetField("packetQueueTask", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var timeout = TimeSpan.FromSeconds(10);

        Client CreateClient() => new(
            context.EventDispatcher, context.Server, NullLoggerFactory.Instance,
            new FakeUserCache(), serverMetrics, context.Provider, pool);

        // CI builds without line numbers: identify the wait and queue state rather than losing the failing phase.
        async Task WaitAsync(Task pending, Client client, string phase)
        {
            try
            {
                await pending.WaitAsync(timeout);
            }
            catch (TimeoutException error)
            {
                var queued = (int)unsent.GetValue(client)!;
                var status = ((Task)consumer.GetValue(client)!).Status;
                throw new TimeoutException(
                    $"Outgoing lifecycle timed out at {phase} (state: {client.State}, unsent: {queued}, "
                        + $"consumer: {status}, connected: {client.Connected}).",
                    error);
            }
        }

        // Every protocol's refusal follows the earlier handler; login also crosses the compression boundary.
        foreach (var state in new[] { ClientState.Login, ClientState.Configuration, ClientState.Play })
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            using var peer = new TcpClient();
            await peer.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
            using var socket = await listener.AcceptSocketAsync();
            using var client = CreateClient();
            client.SetState(state);
            await client.ConnectAsync(socket);
            var stalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            IClientboundPacket first = state == ClientState.Login
                ? new CustomQueryPacket
                {
                    MessageId = 7,
                    Channel = "test:before",
                    Payload = ReadOnlyMemory<byte>.Empty
                }
                : state == ClientState.Play ? CustomPayloadPacket.ClientboundPlay with
                {
                    Channel = "test:before",
                    PluginData = new byte[128_000]
                }
                : CustomPayloadPacket.ClientboundConfiguration with
                {
                    Channel = "test:before",
                    PluginData = new byte[128_000]
                };
            context.EventDispatcher.RegisterEvent(null, (Func<QueuePacketEventArgs, ValueTask>)(async args =>
            {
                if (ReferenceEquals(args.Client, client) && ReferenceEquals(args.Packet, first))
                {
                    stalled.TrySetResult();
                    await resume.Task;
                }
            }));

            try
            {
                await client.QueuePacketAsync(first);
                await WaitAsync(stalled.Task, client, $"{state}: first queue handler");
                using var expected = new NetworkBuffer();
                expected.WritePacket(first);
                if (state == ClientState.Login)
                {
                    typeof(Client).GetMethod("EnableCompression", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(client, null);
                    Assert.False(client.CompressionEnabled);
                    expected.WritePacket(new LoginCompressionPacket(256));
                    Assert.True(client.SendPacket(first));
                    expected.WriteCompressedPacket(first, 256);
                }

                Assert.Equal(0, peer.Available);
                var flush = client.FlushAsync();
                Assert.True(client.DisconnectAsync("Ordered goodbye").IsCompletedSuccessfully);
                Assert.False(client.SendPacket(first));
                IClientboundPacket reason = state == ClientState.Login
                    ? new LoginDisconnectPacket
                    {
                        ReasonJson = ((ChatMessage)"Ordered goodbye").ToString(Globals.JsonOptions)
                    }
                    : state == ClientState.Play ? DisconnectPacket.ClientboundPlay with { Reason = "Ordered goodbye" }
                    : DisconnectPacket.ClientboundConfiguration with { Reason = "Ordered goodbye" };
                if (state == ClientState.Login)
                    expected.WriteCompressedPacket(reason, 256);
                else
                    expected.WritePacket(reason);

                resume.TrySetResult();
                var received = new byte[expected.Size];
                await WaitAsync(peer.GetStream().ReadExactlyAsync(received).AsTask(), client, $"{state}: ordered bytes");
                Assert.Equal(expected.AsSpan(0, expected.Size).ToArray(), received);

                var eof = peer.GetStream().ReadAsync(new byte[1]).AsTask();
                await WaitAsync(eof, client, $"{state}: peer EOF");
                Assert.Equal(0, await eof);

                await WaitAsync(flush, client, $"{state}: flush marker");
                await WaitAsync((Task)consumer.GetValue(client)!, client, $"{state}: queue consumer");
                Assert.Equal(0, (int)unsent.GetValue(client)!);
            }
            finally
            {
                resume.TrySetResult();
            }
        }

        // A stuck plugin cannot retain queued packets or a flush marker after disposal, overflow, or a kick deadline.
        foreach (var ending in new[] { "dispose", "overflow", "deadline" })
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            using var peer = new TcpClient();
            await peer.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
            using var client = CreateClient();
            await client.ConnectAsync(await listener.AcceptSocketAsync());
            client.SetState(ClientState.Login);
            var stalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            context.EventDispatcher.RegisterEvent(null, (Func<QueuePacketEventArgs, ValueTask>)(async args =>
            {
                if (ReferenceEquals(args.Client, client))
                {
                    stalled.TrySetResult();
                    await resume.Task;
                    args.Cancel();
                }
            }));

            try
            {
                var packet = new LoginCompressionPacket(256);
                await client.QueuePacketAsync(packet);
                await WaitAsync(stalled.Task, client, $"{ending}: first queue handler");
                var flush = client.FlushAsync();
                for (var i = 0; i < 20_000; i++)
                    Assert.True(client.QueuePacketAsync(packet).IsCompletedSuccessfully);

                Assert.False(flush.IsCompleted);
                if (ending == "dispose")
                {
                    client.Dispose();
                }
                else if (ending == "overflow")
                {
                    for (var i = 20_002; i < 65_537; i++)
                        Assert.True(client.QueuePacketAsync(packet).IsCompletedSuccessfully);
                }
                else
                {
                    Assert.True(client.DisconnectAsync("Deadline goodbye").IsCompletedSuccessfully);
                }

                await WaitAsync((Task)consumer.GetValue(client)!, client, $"{ending}: queue consumer");
                await WaitAsync(flush, client, $"{ending}: flush marker");
                Assert.Equal(0, (int)unsent.GetValue(client)!);

                var eof = peer.GetStream().ReadAsync(new byte[1]).AsTask();
                await WaitAsync(eof, client, $"{ending}: peer EOF");
                Assert.Equal(0, await eof);
            }
            finally
            {
                resume.TrySetResult();
            }
        }

        // The consumer must also finish cleanly when disposal wins the constructor's Task.Run race.
        for (var i = 0; i < 32; i++)
        {
            using var client = CreateClient();
            client.Dispose();
            await WaitAsync((Task)consumer.GetValue(client)!, client, $"constructor disposal {i}: queue consumer");
        }
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task APreviousSocketsCompletionCannotCloseTheNextClient()
    {
        await using var context = TestServerFactory.CreateServer($"socket-reuse-{Guid.NewGuid():N}");
        using var metrics = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var serverMetrics = new ServerMetrics(metrics.GetRequiredService<IMeterFactory>());
        var pool = new PausedSocketEventPool();
        var timeout = TimeSpan.FromSeconds(10);

        Client CreateClient() => new(
            context.EventDispatcher, context.Server, NullLoggerFactory.Instance,
            new FakeUserCache(), serverMetrics, context.Provider, pool);

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var previousPeer = new TcpClient();
        await previousPeer.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
        using var previous = CreateClient();
        await previous.ConnectAsync(await listener.AcceptSocketAsync());

        try
        {
            previous.Disconnect();

            // The runtime has finished the old receive, but has not read its Completed subscribers yet.
            // Rent the next client's args in that window, as a delayed socket callback can do on a busy runner.
            await pool.Paused.Entered.Task.WaitAsync(timeout);

            using var peer = new TcpClient();
            await peer.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
            using var client = CreateClient();
            client.SetState(ClientState.Login);
            await client.ConnectAsync(await listener.AcceptSocketAsync());

            pool.Paused.Resume.TrySetResult();
            await pool.Paused.Finished.Task.WaitAsync(timeout);

            var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            context.EventDispatcher.RegisterEvent(null, (Func<QueuePacketEventArgs, ValueTask>)(args =>
            {
                if (ReferenceEquals(args.Client, client))
                    delivered.TrySetResult();

                return ValueTask.CompletedTask;
            }));

            var packet = new LoginCompressionPacket(256);
            await client.QueuePacketAsync(packet);
            await delivered.Task.WaitAsync(timeout);
            Assert.True(client.Connected);

            using var expected = new NetworkBuffer();
            expected.WritePacket(packet);
            var received = new byte[expected.Size];
            await peer.GetStream().ReadExactlyAsync(received).AsTask().WaitAsync(timeout);
            Assert.Equal(expected.AsSpan(0, expected.Size).ToArray(), received);
        }
        finally
        {
            pool.Paused.Resume.TrySetResult();
        }
    }

    private sealed class PausedSocketEventPool : ObjectPool<SocketAsyncEventArgs>
    {
        private readonly ConcurrentQueue<SocketAsyncEventArgs> available = new();

        public PausedSocketEventArgs Paused { get; } = new();

        public PausedSocketEventPool()
        {
            this.available.Enqueue(this.Paused);
            this.available.Enqueue(new SocketAsyncEventArgs());
        }

        public override SocketAsyncEventArgs Get() =>
            this.available.TryDequeue(out var args) ? args : new SocketAsyncEventArgs();

        public override void Return(SocketAsyncEventArgs args) => this.available.Enqueue(args);
    }

    private sealed class PausedSocketEventArgs : SocketAsyncEventArgs
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override void OnCompleted(SocketAsyncEventArgs args)
        {
            if (this.Entered.TrySetResult())
            {
                this.Resume.Task.GetAwaiter().GetResult();
                base.OnCompleted(args);
                this.Finished.TrySetResult();
                return;
            }

            base.OnCompleted(args);
        }
    }

    [Fact]
    public async Task PluginItemsAreSyncedToNeoForgeClientsAndRefuseOthers()
    {
        // NeoForge's snapshot maps ids to identifiers, so it needn't be dense.
        SyncedRegistry items = new("minecraft:item", [(0, "minecraft:air"), (2, "test:gem")]);
        IReadOnlyList<SyncedRegistry> Modded() => [items];

        // A vanilla client can't learn the plugin's item.
        var vanilla = new TestClient(new FakeServer());
        vanilla.SetState(ClientState.Configuration);
        var vanillaChannels = new ClientChannels(vanilla, new PayloadChannelRegistry(), Modded);

        vanillaChannels.StartConfiguration();

        Assert.False(await vanillaChannels.FinishHandshakeAsync(0));
        Assert.False(vanilla.Connected);

        // A NeoForge client declares NeoForge's registry sync channels, and is sent the registry before configuration
        // goes on, which it does once the client applied it.
        var client = new TestClient(new FakeServer());
        client.SetState(ClientState.Configuration);
        var channels = new ClientChannels(client, new PayloadChannelRegistry(), Modded);

        channels.StartConfiguration();
        client.ClearPackets();

        var query = new Dictionary<NetworkProtocol, List<NetworkComponent>>
        {
            [NetworkProtocol.Configuration] = [.. ChannelPayloads.BuiltinComponents]
        };
        var queryPayload = Write(writer => ChannelPayloads.WriteQuery(writer, query));
        await channels.HandlePayloadAsync(ChannelPayloads.NeoForgeQuery, queryPayload);

        Assert.False(await channels.FinishHandshakeAsync(0));
        Assert.True(client.Connected);

        Assert.Equal(Write(writer =>
        {
            writer.WriteVarInt(1);
            writer.WriteString("minecraft:item");
        }), PayloadOn(client, ChannelPayloads.RegistrySyncStart));

        // NeoForge's registry snapshot: the registry, each id with its identifier, then no aliases.
        Assert.Equal(Write(writer =>
        {
            writer.WriteString("minecraft:item");
            writer.WriteVarInt(2);
            writer.WriteVarInt(0);
            writer.WriteString("minecraft:air");
            writer.WriteVarInt(2);
            writer.WriteString("test:gem");
            writer.WriteVarInt(0);
        }), PayloadOn(client, ChannelPayloads.RegistrySnapshot));
        Assert.Empty(PayloadOn(client, ChannelPayloads.RegistrySyncCompleted));

        Assert.False(channels.TakeReadyForConfiguration());
        await channels.HandlePayloadAsync(ChannelPayloads.RegistrySyncCompleted, Array.Empty<byte>());
        Assert.True(channels.TakeReadyForConfiguration());
        Assert.False(channels.TakeReadyForConfiguration());

        // A NeoForge client without the sync channels would get the plugin's item ids wrong.
        var withoutSync = new TestClient(new FakeServer());
        withoutSync.SetState(ClientState.Configuration);
        var withoutSyncChannels = new ClientChannels(withoutSync, new PayloadChannelRegistry(), Modded);

        withoutSyncChannels.StartConfiguration();
        await withoutSyncChannels.HandlePayloadAsync(ChannelPayloads.NeoForgeQuery, new byte[] { 0 });

        Assert.False(await withoutSyncChannels.FinishHandshakeAsync(0));
        Assert.False(withoutSync.Connected);
    }

    [Fact(DisplayName = "A client without NeoForge only joins when the server's channels are optional")]
    public async Task OtherClientHandshake()
    {
        var registry = new PayloadChannelRegistry();
        var plugin = new FakePluginContainer();

        registry.Register<EchoPayload>(plugin, PayloadPhase.Configuration, PayloadDirection.Serverbound,
            (_, _) => ValueTask.CompletedTask, version: "1", optional: true);

        var optionalOnly = await HandshakeWithoutNeoForgeAsync(registry);

        // Like NeoForge's server, it announces the optional channels it receives on, its own registry sync's too.
        Assert.True(optionalOnly.Continued);
        Assert.Equal(
            "minecraft:register\0minecraft:unregister\0neoforge:register\0test:echo\0"
                + "neoforge:frozen_registry_sync_completed\0",
            Encoding.ASCII.GetString(PayloadOn(optionalOnly.Client, ChannelPayloads.Register)));

        // An optional channel still has to be registered by the client before it may be used.
        Assert.False(optionalOnly.Channels.HasChannel(EchoPayload.Channel));
        var register = Encoding.ASCII.GetBytes("test:echo\0");
        await optionalOnly.Channels.HandlePayloadAsync(ChannelPayloads.Register, register);
        Assert.True(optionalOnly.Channels.HasChannel(EchoPayload.Channel));

        registry.Register<RequiredPayload>(plugin, PayloadPhase.Play, PayloadDirection.Clientbound, null, "1", false);

        var required = await HandshakeWithoutNeoForgeAsync(registry);

        Assert.False(required.Continued);
        Assert.False(required.Client.Connected);
    }

    [Fact(DisplayName = "A Fabric client tells the server its channels, then gets plugins' items with Fabric's sync")]
    public async Task FabricClientHandshake()
    {
        var registry = new PayloadChannelRegistry();
        var received = new List<string>();

        registry.Register<RequiredPayload>(new FakePluginContainer(), PayloadPhase.Play, PayloadDirection.Clientbound,
            null, "1", optional: false);
        registry.Register<EchoPayload>(new FakePluginContainer(), PayloadPhase.Play, PayloadDirection.Serverbound,
            (payload, _) =>
            {
                received.Add(payload.Text);
                return ValueTask.CompletedTask;
            }, "1", optional: true);

        // Plugins from two namespaces, one registering an item after the other's.
        SyncedRegistry items = new("minecraft:item",
        [
            (0, "minecraft:air"),
            (1, "minecraft:stone"),
            (2, "test:gem"),
            (3, "other:dust"),
            (4, "test:ruby")
        ]);

        async Task<(TestClient Client, ClientChannels Channels)> HandshakeAsync(params string[] playChannels)
        {
            var client = new TestClient(new FakeServer());
            client.SetState(ClientState.Configuration);
            var channels = new ClientChannels(client, registry, () => [items]);

            channels.StartConfiguration();
            client.ClearPackets();

            // Fabric API answers the server's minecraft:register with its configuration channels, before the pong.
            var register = Encoding.ASCII.GetBytes("c:version\0c:register\0fabric:registry/sync\0");
            await channels.HandlePayloadAsync(ChannelPayloads.Register, register);

            Assert.False(await channels.FinishHandshakeAsync(0));
            Assert.Equal(Write(writer =>
            {
                writer.WriteVarInt(1);
                writer.WriteVarInt(1);
            }), PayloadOn(client, ChannelPayloads.CommonVersion));

            await channels.HandlePayloadAsync(ChannelPayloads.CommonVersion, Write(writer =>
            {
                writer.WriteVarInt(1);
                writer.WriteVarInt(1);
            }));

            // The server's play channels it receives on.
            Assert.Equal(Write(writer =>
            {
                writer.WriteVarInt(1);
                writer.WriteString("play");
                writer.WriteVarInt(1);
                writer.WriteString(EchoPayload.Channel);
            }), PayloadOn(client, ChannelPayloads.CommonRegister));

            await channels.HandlePayloadAsync(ChannelPayloads.CommonRegister, Write(writer =>
            {
                writer.WriteVarInt(1);
                writer.WriteString("play");
                writer.WriteVarInt(playChannels.Length);

                foreach (var channel in playChannels)
                    writer.WriteString(channel);
            }));

            return (client, channels);
        }

        var (fabric, fabricChannels) = await HandshakeAsync(RequiredPayload.Channel);

        Assert.True(fabric.Connected);
        Assert.True(fabricChannels.CanUse(RequiredPayload.Channel, NetworkProtocol.Play));
        Assert.False(fabricChannels.TakeReadyForConfiguration());

        // Fabric's encoding: registries by namespace ("minecraft" written empty), then each registry's path, attributes
        // and entries by namespace, in runs of consecutive ids starting at their offset from the previous run's end.
        Assert.Equal(Write(writer =>
        {
            writer.WriteVarInt(1);
            writer.WriteString("");
            writer.WriteVarInt(1);
            writer.WriteString("item");
            writer.WriteByte((byte)0);
            writer.WriteVarInt(3);

            writer.WriteString("");
            writer.WriteVarInt(1);
            writer.WriteVarInt(0);
            writer.WriteVarInt(2);
            writer.WriteString("air");
            writer.WriteString("stone");

            writer.WriteString("test");
            writer.WriteVarInt(2);
            writer.WriteVarInt(1);
            writer.WriteVarInt(1);
            writer.WriteString("gem");
            writer.WriteVarInt(2);
            writer.WriteVarInt(1);
            writer.WriteString("ruby");

            writer.WriteString("other");
            writer.WriteVarInt(1);
            writer.WriteVarInt(-1);
            writer.WriteVarInt(1);
            writer.WriteString("dust");
        }), PayloadOn(fabric, ChannelPayloads.FabricRegistrySync));

        await fabricChannels.HandlePayloadAsync(ChannelPayloads.FabricRegistrySyncCompleted, Array.Empty<byte>());
        Assert.True(fabricChannels.TakeReadyForConfiguration());

        // The client never registers a channel only the server receives on, but may send on it.
        fabric.SetState(ClientState.Play);
        await fabricChannels.HandlePayloadAsync(EchoPayload.Channel, Write(new EchoPayload("hello").Write));
        Assert.Equal(["hello"], received);

        // Without the mod that receives the server's required channel, the client is refused.
        var (withoutMod, _) = await HandshakeAsync();

        Assert.False(withoutMod.Connected);

        // A refusal is final: payloads the client sent after it, like a version the server does support, are ignored.
        var unsupported = new TestClient(new FakeServer());
        unsupported.SetState(ClientState.Configuration);
        var unsupportedChannels = new ClientChannels(unsupported, registry, () => [items]);

        unsupportedChannels.StartConfiguration();
        await unsupportedChannels.HandlePayloadAsync(
            ChannelPayloads.Register,
            Encoding.ASCII.GetBytes("c:version\0c:register\0"));
        await unsupportedChannels.FinishHandshakeAsync(0);
        unsupported.ClearPackets();

        await unsupportedChannels.HandlePayloadAsync(ChannelPayloads.CommonVersion, Write(writer =>
        {
            writer.WriteVarInt(1);
            writer.WriteVarInt(2);
        }));
        await unsupportedChannels.HandlePayloadAsync(ChannelPayloads.CommonVersion, Write(writer =>
        {
            writer.WriteVarInt(1);
            writer.WriteVarInt(1);
        }));

        Assert.False(unsupported.Connected);
        Assert.DoesNotContain(
            unsupported.SentPackets.OfType<CustomPayloadPacket>(),
            packet => packet.Channel == ChannelPayloads.CommonRegister);
    }

    private static async Task<(TestClient Client, ClientChannels Channels, bool Continued)>
        HandshakeWithoutNeoForgeAsync(PayloadChannelRegistry registry)
    {
        var client = new TestClient(new FakeServer());
        client.SetState(ClientState.Configuration);
        var channels = new ClientChannels(client, registry);

        channels.StartConfiguration();
        client.ClearPackets();

        var continued = await channels.FinishHandshakeAsync(0);

        return (client, channels, continued);
    }

    private static byte[] PayloadOn(TestClient client, string channel) => client.SentPackets
        .OfType<CustomPayloadPacket>()
        .Last(packet => packet.Channel == channel)
        .PluginData
        .ToArray();

    private static byte[] Write(Action<INetStreamWriter> write)
    {
        using var buffer = new NetworkBuffer();
        write(buffer);

        return buffer.AsSpan(0, buffer.Size).ToArray();
    }

    private static List<NetworkComponent> ParseComponents(string spec) => spec
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Select(ParseComponent)
        .ToList();

    private static NetworkComponent ParseComponent(string spec)
    {
        var optional = spec.EndsWith('?');
        spec = spec.TrimEnd('?');

        PacketFlow? flow = spec[^1] switch
        {
            '>' => PacketFlow.Clientbound,
            '<' => PacketFlow.Serverbound,
            _ => null
        };
        spec = spec.TrimEnd('>', '<');

        var parts = spec.Split('=');

        return new NetworkComponent(parts[0], parts[1], flow, optional);
    }

    public sealed record EchoPayload(string Text) : ICustomPayload<EchoPayload>
    {
        public static string Channel => "test:echo";

        public static EchoPayload Read(INetStreamReader reader) => new(reader.ReadString());

        public void Write(INetStreamWriter writer) => writer.WriteString(this.Text);
    }

    public sealed record RequiredPayload : ICustomPayload<RequiredPayload>
    {
        public static string Channel => "test:required";

        public static RequiredPayload Read(INetStreamReader reader) => new();

        public void Write(INetStreamWriter writer) { }
    }

    private sealed class FakePluginContainer : IPluginContainer
    {
        public PluginBase Plugin => null;
        public IServiceScope ServiceScope => null;
        public Assembly PluginAssembly => typeof(ModdedNetworking).Assembly;

        public void InjectServices(ILogger logger, object module) { }

        public byte[] GetFileData(string fileName) => null;
    }
}
