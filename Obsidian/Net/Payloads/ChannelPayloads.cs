using System.Text;

namespace Obsidian.Net.Payloads;

/// <summary>
/// The ids and encodings of the payloads that set up payload channels: vanilla-era <c>minecraft:register</c> and
/// <c>minecraft:unregister</c>, NeoForge's handshake, the common channels NeoForge and Fabric share, and Fabric's
/// registry sync. The encodings match what NeoForge 21.11 and Fabric API for 1.21.11 read and write, field for field,
/// since their clients decode them.
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
    public const string CommonVersion = "c:version";
    public const string CommonRegister = "c:register";
    public const string FabricRegistrySync = "fabric:registry/sync";
    public const string FabricRegistrySyncCompleted = "fabric:registry/sync/complete";

    /// <summary>
    /// The version of the common channels the server speaks, the only one there is.
    /// </summary>
    public const int CommonNetworkingVersion = 1;

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
    /// Reads a <c>neoforge:network</c> payload; see <see cref="WriteNetworkSetup"/>.
    /// </summary>
    /// <returns>Per protocol, each agreed channel id with its version.</returns>
    public static Dictionary<NetworkProtocol, Dictionary<string, string>> ReadNetworkSetup(INetStreamReader reader)
    {
        var setup = new Dictionary<NetworkProtocol, Dictionary<string, string>>();
        var protocolCount = reader.ReadVarInt();

        for (var i = 0; i < protocolCount; i++)
        {
            var protocol = (NetworkProtocol)reader.ReadVarInt();
            var channelCount = reader.ReadVarInt();
            var channels = new Dictionary<string, string>();

            for (var j = 0; j < channelCount; j++)
            {
                reader.ReadString();
                var id = reader.ReadString();
                channels[id] = reader.ReadString();
            }

            setup[protocol] = channels;
        }

        return setup;
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
    /// Reads a <c>neoforge:frozen_registry_sync_start</c> payload; see <see cref="WriteRegistrySyncStart"/>.
    /// </summary>
    public static List<string> ReadRegistrySyncStart(INetStreamReader reader)
    {
        var count = reader.ReadVarInt();
        var names = new List<string>();

        for (var i = 0; i < count; i++)
            names.Add(reader.ReadString());

        return names;
    }

    /// <summary>
    /// Reads a <c>neoforge:frozen_registry</c> payload; see <see cref="WriteRegistrySnapshot"/>. Aliases, which name
    /// an entry by another identifier, are skipped.
    /// </summary>
    public static SyncedRegistry ReadRegistrySnapshot(INetStreamReader reader)
    {
        var name = reader.ReadString();
        var count = reader.ReadVarInt();
        var entries = new List<(int Id, string Identifier)>();

        for (var i = 0; i < count; i++)
            entries.Add((reader.ReadVarInt(), reader.ReadString()));

        var aliases = reader.ReadVarInt();
        for (var i = 0; i < aliases; i++)
        {
            reader.ReadString();
            reader.ReadString();
        }

        return new SyncedRegistry(name, entries);
    }

    /// <summary>
    /// Writes a <c>c:version</c> payload: the common networking versions the sender supports.
    /// </summary>
    public static void WriteCommonVersion(INetStreamWriter writer, IReadOnlyList<int> versions)
    {
        writer.WriteVarInt(versions.Count);

        foreach (var version in versions)
            writer.WriteVarInt(version);
    }

    /// <summary>
    /// Reads a <c>c:version</c> payload; see <see cref="WriteCommonVersion"/>.
    /// </summary>
    public static List<int> ReadCommonVersion(INetStreamReader reader)
    {
        var count = reader.ReadVarInt();
        var versions = new List<int>();

        for (var i = 0; i < count; i++)
            versions.Add(reader.ReadVarInt());

        return versions;
    }

    /// <summary>
    /// Writes a <c>c:register</c> payload: the common networking version, the phase the channels are for
    /// (<c>configuration</c> or <c>play</c>), then the channels the sender receives payloads on in it.
    /// </summary>
    public static void WriteCommonRegister(
        INetStreamWriter writer,
        NetworkProtocol protocol,
        IReadOnlyCollection<string> channels)
    {
        writer.WriteVarInt(CommonNetworkingVersion);
        writer.WriteString(protocol == NetworkProtocol.Play ? "play" : "configuration");
        writer.WriteVarInt(channels.Count);

        foreach (var channel in channels)
            writer.WriteString(channel);
    }

    /// <summary>
    /// Reads a <c>c:register</c> payload; see <see cref="WriteCommonRegister"/>. The protocol is null for a phase
    /// other than configuration and play.
    /// </summary>
    public static (int Version, NetworkProtocol? Protocol, List<string> Channels) ReadCommonRegister(
        INetStreamReader reader)
    {
        var version = reader.ReadVarInt();
        NetworkProtocol? protocol = reader.ReadString() switch
        {
            "play" => NetworkProtocol.Play,
            "configuration" => NetworkProtocol.Configuration,
            _ => null
        };

        var count = reader.ReadVarInt();
        var channels = new List<string>();

        for (var i = 0; i < count; i++)
            channels.Add(reader.ReadString());

        return (version, protocol, channels);
    }

    /// <summary>
    /// Writes a <c>fabric:registry/sync</c> payload, which tells a Fabric client the id of every entry of the
    /// registries plugins added to. Registries are grouped by namespace, then each registry's entries by namespace,
    /// and those in runs of consecutive ids, so that a run's identifiers follow each other without their ids.
    /// </summary>
    /// <remarks>
    /// Fabric API's encoding, which its client decodes: <c>minecraft</c> is written as an empty namespace, a registry
    /// has an attribute byte (0: not optional, so a client without the registry refuses to join), and a run starts at
    /// its offset from the end of the registry's previous run, which is negative when namespaces interleave.
    /// </remarks>
    public static void WriteFabricRegistrySync(INetStreamWriter writer, IReadOnlyList<SyncedRegistry> registries)
    {
        var byNamespace = registries
            .GroupBy(registry => SplitIdentifier(registry.Name).Namespace)
            .ToList();

        writer.WriteVarInt(byNamespace.Count);

        foreach (var group in byNamespace)
        {
            writer.WriteString(ShortNamespace(group.Key));
            writer.WriteVarInt(group.Count());

            foreach (var registry in group)
            {
                writer.WriteString(SplitIdentifier(registry.Name).Path);
                writer.WriteByte((byte)0);
                WriteFabricEntries(writer, registry.Entries);
            }
        }
    }

    private static void WriteFabricEntries(INetStreamWriter writer, IReadOnlyList<(int Id, string Identifier)> entries)
    {
        var byNamespace = entries
            .OrderBy(entry => entry.Id)
            .Select(entry => (entry.Id, Identifier: SplitIdentifier(entry.Identifier)))
            .GroupBy(entry => entry.Identifier.Namespace)
            .ToList();

        writer.WriteVarInt(byNamespace.Count);

        var previousId = 0;

        foreach (var group in byNamespace)
        {
            var runs = new List<List<(int Id, string Path)>>();

            foreach (var (id, identifier) in group)
            {
                if (runs.Count == 0 || runs[^1][^1].Id + 1 != id)
                    runs.Add([]);

                runs[^1].Add((id, identifier.Path));
            }

            writer.WriteString(ShortNamespace(group.Key));
            writer.WriteVarInt(runs.Count);

            foreach (var run in runs)
            {
                writer.WriteVarInt(run[0].Id - previousId);
                writer.WriteVarInt(run.Count);

                foreach (var (_, path) in run)
                    writer.WriteString(path);

                previousId = run[^1].Id;
            }
        }
    }

    private static (string Namespace, string Path) SplitIdentifier(string identifier)
    {
        var colon = identifier.IndexOf(':');

        return colon < 0 ? ("minecraft", identifier) : (identifier[..colon], identifier[(colon + 1)..]);
    }

    private static string ShortNamespace(string @namespace) => @namespace == "minecraft" ? "" : @namespace;

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
