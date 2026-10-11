using System.Text;

namespace Obsidian.Net.Payloads;

/// <summary>
/// The ids and encodings of the payloads that set up payload channels: vanilla-era <c>minecraft:register</c> and
/// <c>minecraft:unregister</c>, and NeoForge's handshake. The encodings match what NeoForge 21.11 reads and writes,
/// field for field, since a NeoForge client decodes them.
/// </summary>
internal static class ChannelPayloads
{
    public const string Brand = "minecraft:brand";
    public const string Register = "minecraft:register";
    public const string Unregister = "minecraft:unregister";
    public const string NeoForgeQuery = "neoforge:register";
    public const string NeoForgeNetwork = "neoforge:network";
    public const string NeoForgeSetupFailed = "neoforge:modded_network_setup_failed";

    /// <summary>
    /// The handshake channels the server always listens on, announced with <c>minecraft:register</c>.
    /// </summary>
    public static ImmutableArray<string> Builtin { get; } = [Register, Unregister, NeoForgeQuery];

    /// <summary>
    /// Encodes a <c>minecraft:register</c> or <c>minecraft:unregister</c> payload: each channel id followed by a zero
    /// byte, up to the end of the payload.
    /// </summary>
    public static byte[] WriteChannelList(IEnumerable<string> channels)
    {
        var bytes = new List<byte>();

        foreach (var channel in channels)
        {
            bytes.AddRange(Encoding.ASCII.GetBytes(channel));
            bytes.Add(0);
        }

        return [.. bytes];
    }

    /// <summary>
    /// Decodes a <c>minecraft:register</c> or <c>minecraft:unregister</c> payload, skipping empty ids.
    /// </summary>
    public static List<string> ReadChannelList(ReadOnlySpan<byte> payload) =>
        Encoding.ASCII.GetString(payload).Split('\0', StringSplitOptions.RemoveEmptyEntries).ToList();

    /// <summary>
    /// Reads a <c>neoforge:register</c> query: a map from protocol to the set of channels the sender declares.
    /// </summary>
    public static Dictionary<NetworkProtocol, List<NetworkComponent>> ReadQuery(INetStreamReader reader)
    {
        // The counts come from the client, so they only bound the loops, not the memory reserved up front.
        const int initialCapacity = 64;

        var query = new Dictionary<NetworkProtocol, List<NetworkComponent>>();
        var protocolCount = reader.ReadVarInt();

        for (var i = 0; i < protocolCount; i++)
        {
            var protocol = (NetworkProtocol)reader.ReadVarInt();
            var componentCount = reader.ReadVarInt();
            var components = new List<NetworkComponent>(Math.Min(componentCount, initialCapacity));

            for (var j = 0; j < componentCount; j++)
            {
                var id = reader.ReadString();
                var version = reader.ReadString();
                PacketFlow? flow = reader.ReadBoolean() ? (PacketFlow)reader.ReadVarInt() : null;
                var optional = reader.ReadBoolean();

                components.Add(new NetworkComponent(id, version, flow, optional));
            }

            query[protocol] = components;
        }

        return query;
    }

    /// <summary>
    /// Writes a <c>neoforge:register</c> query; see <see cref="ReadQuery"/>.
    /// </summary>
    public static void WriteQuery(
        INetStreamWriter writer,
        IReadOnlyDictionary<NetworkProtocol, List<NetworkComponent>> query)
    {
        writer.WriteVarInt(query.Count);

        foreach (var (protocol, components) in query)
        {
            writer.WriteVarInt((int)protocol);
            writer.WriteVarInt(components.Count);

            foreach (var component in components)
            {
                writer.WriteString(component.Id);
                writer.WriteString(component.Version);
                writer.WriteBoolean(component.Flow is not null);

                if (component.Flow is PacketFlow flow)
                    writer.WriteVarInt((int)flow);

                writer.WriteBoolean(component.Optional);
            }
        }
    }

    /// <summary>
    /// Writes a <c>neoforge:network</c> payload: per protocol, a map from each agreed channel id to the channel (its
    /// id again, then the chosen version).
    /// </summary>
    public static void WriteNetworkSetup(
        INetStreamWriter writer,
        IReadOnlyDictionary<NetworkProtocol, IReadOnlyDictionary<string, string>> setup)
    {
        writer.WriteVarInt(setup.Count);

        foreach (var (protocol, channels) in setup)
        {
            writer.WriteVarInt((int)protocol);
            writer.WriteVarInt(channels.Count);

            foreach (var (id, version) in channels)
            {
                writer.WriteString(id);
                writer.WriteString(id);
                writer.WriteString(version);
            }
        }
    }

    /// <summary>
    /// Writes a <c>neoforge:modded_network_setup_failed</c> payload: a map from channel id to the reason as a text
    /// component.
    /// </summary>
    public static void WriteSetupFailed(INetStreamWriter writer, IReadOnlyDictionary<string, string> reasons)
    {
        writer.WriteVarInt(reasons.Count);

        foreach (var (id, reason) in reasons)
        {
            writer.WriteString(id);
            writer.WriteChat(ChatMessage.Simple(reason));
        }
    }
}
