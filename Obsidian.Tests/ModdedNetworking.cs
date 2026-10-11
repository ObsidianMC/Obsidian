using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Obsidian.API;
using Obsidian.API.Networking;
using Obsidian.API.Plugins;
using Obsidian.Net;
using Obsidian.Net.Packets.Common;
using Obsidian.Net.Payloads;
using Obsidian.Tests.Fakes;
using Obsidian.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Plugin payload channels and NeoForge's configuration handshake.
/// </summary>
public class ModdedNetworking
{
    // Each side's channels: "id=version", then ">" for clientbound or "<" for serverbound, then "?" when optional.
    // The expected outcome lists the agreed channels as "id=version", or the refused ones as "!id".
    [Theory(DisplayName = "Channels are negotiated with NeoForge's rules")]
    [InlineData("mod:a=1", "mod:a=1", "mod:a=1")]
    [InlineData("mod:a=2", "mod:a=1", "!mod:a")]
    [InlineData("mod:a=1>", "mod:a=1", "!mod:a")]
    [InlineData("mod:a=1>", "mod:a=1<", "!mod:a")]
    [InlineData("mod:a=1<", "mod:a=1<", "mod:a=1")]
    [InlineData("mod:a=1?", "", "")]
    [InlineData("mod:a=1", "", "!mod:a")]
    [InlineData("", "mod:a=1", "!mod:a")]
    [InlineData("", "mod:a=1?", "")]
    [InlineData("mod:a=1 mod:b=1?", "mod:a=1 mod:c=1?", "mod:a=1")]
    [InlineData("mod:a=1 mod:b=1", "mod:a=1", "!mod:b")]
    public void NegotiatesChannels(string server, string client, string expected)
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
    }

    [Fact]
    public async Task PluginItemsAreSyncedToNeoForgeClientsAndRefuseOthers()
    {
        RegistrySnapshot items = new("minecraft:item", ["minecraft:air", "test:gem"]);
        IReadOnlyList<RegistrySnapshot> Modded() => [items];

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
            writer.WriteVarInt(1);
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

    [Fact]
    public async Task MalformedQueryRefusesClient()
    {
        var registry = new PayloadChannelRegistry();
        registry.Register<EchoPayload>(new FakePluginContainer(), PayloadPhase.Play, PayloadDirection.Bidirectional,
            null, "1", optional: false);

        var client = new TestClient(new FakeServer());
        client.SetState(ClientState.Configuration);
        var channels = new ClientChannels(client, registry);

        channels.StartConfiguration();

        // Claims one protocol, then ends: the client is refused rather than treated as a vanilla one.
        await channels.HandlePayloadAsync(ChannelPayloads.NeoForgeQuery, new byte[] { 1 });

        Assert.False(client.Connected);
        Assert.False(await channels.FinishHandshakeAsync(0));
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

    [Fact(DisplayName = "The status response says whether the server is modded")]
    public void StatusReportsModded()
    {
        var status = new ServerStatus(new FakeServer(), serverIcon: "")
        {
            IsModded = true
        };

        Assert.Contains("\"isModded\": true", status.ToJson());
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
