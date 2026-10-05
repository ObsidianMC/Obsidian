using System.Text.Json;
using System.Text.RegularExpressions;

namespace Obsidian.SourceGenerators.Packets;

/// <summary>
/// The packets and the vanilla types they write, as the asset generator dumps them: <c>packets.json</c> (each packet's
/// fields, with how vanilla writes them) and <c>packet_types.json</c> (the records and enums those fields refer to).
/// </summary>
internal sealed class Protocol
{
    /// <summary>The namespace generated vanilla types are emitted into, in Obsidian.API.</summary>
    public const string TypesNamespace = "Obsidian.API";

    // Vanilla names that read badly as C# types: a typo, and the name most developers know the type by.
    private static readonly Dictionary<string, string> renames = new()
    {
        ["ChatVisiblity"] = "ChatVisibility",
        ["GameType"] = "GameMode",
    };

    public IReadOnlyList<ProtocolPacket> Packets { get; }

    /// <summary>The vanilla types by their Mojang name (<c>Outer.Inner</c>).</summary>
    public IReadOnlyDictionary<string, ProtocolType> Types { get; }

    private Protocol(IReadOnlyList<ProtocolPacket> packets, IReadOnlyDictionary<string, ProtocolType> types)
    {
        Packets = packets;
        Types = types;
    }

    public static Protocol Parse(string packetsJson, string typesJson)
    {
        var packets = new List<ProtocolPacket>();
        using (var document = JsonDocument.Parse(packetsJson))
        {
            foreach (var packet in document.RootElement.EnumerateArray())
            {
                packets.Add(new ProtocolPacket(
                    packet.GetProperty("name").GetString()!,
                    packet.GetProperty("resource_id").GetString()!,
                    packet.GetProperty("namespace").GetString()!,
                    packet.GetProperty("state").GetString()!,
                    ParseFields(packet.GetProperty("fields"))));
            }
        }

        var types = new Dictionary<string, ProtocolType>();
        using (var document = JsonDocument.Parse(typesJson))
        {
            foreach (var type in document.RootElement.EnumerateObject())
            {
                var isEnum = type.Value.GetProperty("kind").GetString() == "enum";
                types[type.Name] = new ProtocolType(
                    type.Name,
                    TypeName(type.Name),
                    isEnum,
                    isEnum ? [] : ParseFields(type.Value.GetProperty("fields")),
                    isEnum ? [.. type.Value.GetProperty("values").EnumerateObject().Select(value => (value.Name, value.Value.GetInt32()))] : []);
            }
        }

        return new Protocol(packets, types);
    }

    private static List<ProtocolField> ParseFields(JsonElement fields)
    {
        static bool Flag(JsonElement field, string name) => field.TryGetProperty(name, out var value) && value.GetBoolean();

        return [.. fields.EnumerateArray().Select(field => new ProtocolField(
            field.GetProperty("name").GetString()!,
            field.GetProperty("type").GetString()!,
            field.TryGetProperty("sent", out var sent) ? sent.GetBoolean() : null,
            field.TryGetProperty("encoding", out var encoding) ? encoding.Clone() : null,
            Flag(field, "conditional"), Flag(field, "repeated"), Flag(field, "packed")))];
    }

    /// <summary>
    /// The C# name of a vanilla type: its own name for a top-level class (<c>GlobalPos</c>), and for a nested one its
    /// outer classes' names without <c>Clientbound</c>/<c>Serverbound</c>/<c>Packet</c> before it
    /// (<c>ClientboundBossEventPacket.OperationType</c> is <c>BossEventOperationType</c>).
    /// </summary>
    public static string TypeName(string mojangName)
    {
        var parts = mojangName.Split('.');
        var name = parts.Length == 1
            ? parts[0]
            : string.Concat(parts.Take(parts.Length - 1).Select(part => Regex.Replace(part, "^(Clientbound|Serverbound)|Packet$", ""))) + parts.Last();
        return renames.TryGetValue(name, out var renamed) ? renamed : name;
    }

    /// <summary><c>gameTime</c> → <c>GameTime</c>, <c>MAIN_HAND</c> → <c>MainHand</c>.</summary>
    public static string PascalCase(string name)
    {
        if (name.Contains('_') || name.ToUpperInvariant() == name)
            return string.Concat(name.Split(['_'], StringSplitOptions.RemoveEmptyEntries)
                .Select(word => char.ToUpperInvariant(word[0]) + word.Substring(1).ToLowerInvariant()));

        return char.ToUpperInvariant(name[0]) + name.Substring(1);
    }
}

/// <param name="Namespace"><c>Clientbound</c> or <c>Serverbound</c>.</param>
internal sealed record ProtocolPacket(string Name, string ResourceId, string Namespace, string State, IReadOnlyList<ProtocolField> Fields);

/// <param name="JavaType">The field's Java type by Mojang's names (<c>int</c>, <c>List&lt;Component&gt;</c>).</param>
/// <param name="Sent">Whether vanilla writes the field; null when the asset generator couldn't tell.</param>
/// <param name="Encoding">How vanilla writes it (a node with a <c>kind</c>, see <see cref="Encodings"/>).</param>
internal sealed record ProtocolField(string Name, string JavaType, bool? Sent, JsonElement? Encoding,
    bool Conditional, bool Repeated, bool Packed);

/// <param name="MojangName">Vanilla's name, <c>Outer.Inner</c> for nested classes.</param>
/// <param name="Name">The generated C# type's name (see <see cref="Protocol.TypeName"/>).</param>
internal sealed record ProtocolType(string MojangName, string Name, bool IsEnum, IReadOnlyList<ProtocolField> Fields,
    IReadOnlyList<(string Name, int Value)> Values);
