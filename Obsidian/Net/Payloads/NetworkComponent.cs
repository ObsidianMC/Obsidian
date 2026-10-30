using Obsidian.API.Networking;

namespace Obsidian.Net.Payloads;

/// <summary>
/// The connection protocols payload channels are negotiated for. The values are vanilla's ConnectionProtocol ordinals
/// (checked against the 26.3 server jar), which NeoForge writes as VarInts in its handshake payloads.
/// </summary>
internal enum NetworkProtocol
{
    Play = 1,
    Configuration = 4
}

/// <summary>
/// A payload direction as NeoForge writes it: vanilla's PacketFlow ordinals (checked against the 26.3 server jar).
/// </summary>
internal enum PacketFlow
{
    Serverbound = 0,
    Clientbound = 1
}

/// <summary>
/// One side's declaration of a payload channel, as exchanged in NeoForge's <c>neoforge:register</c> query.
/// </summary>
/// <param name="Flow">The only direction the channel carries payloads, or null for both.</param>
internal readonly record struct NetworkComponent(string Id, string Version, PacketFlow? Flow, bool Optional);

internal static class NetworkProtocolExtensions
{
    public static bool Includes(this PayloadPhase phase, NetworkProtocol protocol) => phase switch
    {
        PayloadPhase.Configuration => protocol == NetworkProtocol.Configuration,
        PayloadPhase.Play => protocol == NetworkProtocol.Play,
        _ => true
    };

    public static PacketFlow? ToFlow(this PayloadDirection direction) => direction switch
    {
        PayloadDirection.Clientbound => PacketFlow.Clientbound,
        PayloadDirection.Serverbound => PacketFlow.Serverbound,
        _ => null
    };

    /// <summary>
    /// The protocol a client in <paramref name="state"/> uses, or null outside configuration and play.
    /// </summary>
    public static NetworkProtocol? ToNetworkProtocol(this ClientState state) => state switch
    {
        ClientState.Configuration => NetworkProtocol.Configuration,
        ClientState.Play => NetworkProtocol.Play,
        _ => null
    };
}
