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
    public const string RegistrySyncStart = "neoforge:frozen_registry_sync_start";
    public const string RegistrySnapshot = "neoforge:frozen_registry";
    public const string RegistrySyncCompleted = "neoforge:frozen_registry_sync_completed";

    /// <summary>
    /// NeoForge's own channels the server takes part in, declared like NeoForge declares them (optional, version 1,
    /// configuration phase) so that negotiating with a NeoForge client keeps them: its registry sync.
    /// </summary>
    public static ImmutableArray<NetworkComponent> BuiltinComponents { get; } =
    [
        new(RegistrySyncStart, "1", PacketFlow.Clientbound, true),
        new(RegistrySnapshot, "1", PacketFlow.Clientbound, true),
        new(RegistrySyncCompleted, "1", null, true)
    ];

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
    /// Writes a <c>neoforge:frozen_registry_sync_start</c> payload: the names of the registries about to be sent.
    /// </summary>
    public static void WriteRegistrySyncStart(INetStreamWriter writer, IReadOnlyList<SyncedRegistry> registries)
    {
        writer.WriteVarInt(registries.Count);

        foreach (var registry in registries)
            writer.WriteString(registry.Name);
    }

    /// <summary>
    /// Writes a <c>neoforge:frozen_registry</c> payload: the registry's name, then NeoForge's registry snapshot, a map
    /// from each entry's id to its identifier and a map of aliases, which Obsidian has none of.
    /// </summary>
    public static void WriteRegistrySnapshot(INetStreamWriter writer, SyncedRegistry registry)
    {
        writer.WriteString(registry.Name);
        writer.WriteVarInt(registry.Entries.Count);

        foreach (var (id, identifier) in registry.Entries)
        {
            writer.WriteVarInt(id);
            writer.WriteString(identifier);
        }

        writer.WriteVarInt(0);
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

/// <summary>
/// A registry that clients must agree with the server on, because plugins added to it.
/// </summary>
/// <param name="Name">The registry, like <c>minecraft:item</c>.</param>
/// <param name="Entries">The registry's entries, by id.</param>
internal sealed record SyncedRegistry(string Name, IReadOnlyList<(int Id, string Identifier)> Entries);
