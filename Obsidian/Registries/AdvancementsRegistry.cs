using Microsoft.Extensions.Logging;
using Obsidian.API.Advancements;
using Obsidian.Plugins;
using System.Collections.Frozen;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;

namespace Obsidian.Registries;

/// <summary>Vanilla-first, plugin-then-data-pack precedence, like loot tables.</summary>
internal static partial class AdvancementsRegistry
{
    private static readonly Lazy<FrozenDictionary<string, Advancement>> vanilla = new(ReadVanilla);
    private static FrozenDictionary<string, Advancement>? current;
    private static readonly ConditionalWeakTable<FrozenDictionary<string, Advancement>, TriggerIndex> indices = new();

    private sealed class TriggerIndex(FrozenDictionary<string, Advancement> definitions)
    {
        public FrozenDictionary<string, Advancement[]> ByTrigger { get; } = definitions.Values
            .SelectMany(definition => definition.Criteria.Select(criterion => criterion.Trigger).Distinct()
                .Select(trigger => (Trigger: trigger!, Definition: definition)))
            .GroupBy(entry => entry.Trigger)
            .ToFrozenDictionary(group => group.Key, group => group.Select(entry => entry.Definition).ToArray());
    }

    public static IReadOnlyList<Advancement> ForTrigger(string identifier)
    {
        var index = indices.GetValue(Current, definitions => new TriggerIndex(definitions));
        return index.ByTrigger.GetValueOrDefault(identifier) ?? [];
    }

    public static FrozenDictionary<string, Advancement> Current => Volatile.Read(ref current) ?? vanilla.Value;

    public static void Apply(IEnumerable<Advancement> files)
    {
        var result = vanilla.Value.ToDictionary();
        foreach (var advancement in files)
            result[advancement.Identifier] = advancement;

        Volatile.Write(ref current, PlaceDisplays(result.Values));
    }

    public static IEnumerable<(string Path, string Identifier)> FindFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths.Order(StringComparer.Ordinal))
        {
            var parts = path.Split('/', 4);
            if (parts.Length == 4 && parts[0] == "data" && parts[2] == "advancement"
                && parts[3].EndsWith(".json", StringComparison.Ordinal))
            {
                var identifier = parts[1] + ":" + parts[3][..^5];
                if (PluginTags.IsFileIdentifier(identifier))
                    yield return (path, identifier);
            }
        }
    }

    public static ImmutableArray<Advancement> Read(DataFiles files, ILogger logger)
    {
        var result = ImmutableArray.CreateBuilder<Advancement>();
        foreach (var (path, identifier) in FindFiles(files.Paths))
        {
            try
            {
                var bytes = files.Read(path) ?? throw new InvalidDataException($"{path} can't be read.");
                if (bytes.Length > files.Budget.Available(path))
                    throw new InvalidDataException($"{path} exceeds the advancement file budget.");

                files.Budget.Charge(path, bytes.Length);
                using var json = JsonDocument.Parse(bytes);
                result.Add(Parse(identifier, json.RootElement));
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or ArgumentException
                or InvalidOperationException or KeyNotFoundException)
            {
                logger.LogWarning(ex, "Could not read advancement {Path} from {Source}", path, files.Location);
            }
        }

        return result.ToImmutable();
    }

    internal static Advancement Parse(string identifier, JsonElement json)
    {
        var criteria = json.GetProperty("criteria").EnumerateObject().Select(property =>
            new Criteria(property.Name, true)
            {
                Trigger = Qualify(property.Value.GetProperty("trigger").GetString()!),
                Conditions = property.Value.TryGetProperty("conditions", out var conditions)
                    ? conditions.Clone()
                    : JsonSerializer.SerializeToElement(new Dictionary<string, object>())
            }).ToList();
        var requirements = json.TryGetProperty("requirements", out var rows)
            ? rows.EnumerateArray().Select(row => row.EnumerateArray()
                .Select(value => value.GetString()!).ToList()).ToList()
            : criteria.Select(criterion => new List<string> { criterion.Identifier }).ToList();
        var names = criteria.Select(criterion => criterion.Identifier).ToHashSet();
        if (names.Count == 0 || requirements.Any(row => row.Count == 0 || row.Any(name => !names.Contains(name)))
            || !names.SetEquals(requirements.SelectMany(row => row)))
        {
            throw new InvalidDataException($"Invalid requirements in {identifier}.");
        }

        AdvancementReward? reward = null;
        if (json.TryGetProperty("rewards", out var rewards))
        {
            reward = new AdvancementReward(
                rewards.TryGetProperty("experience", out var xp) ? xp.GetInt32() : 0,
                Strings(rewards, "loot"), Strings(rewards, "recipes"))
            {
                Function = rewards.TryGetProperty("function", out var function) ? function.GetString() : null
            };
        }

        return new Advancement(identifier,
            json.TryGetProperty("parent", out var parent) ? Qualify(parent.GetString()!) : "",
            json.TryGetProperty("display", out var display) ? ReadDisplay(display) : null, reward, criteria)
        {
            Requirements = requirements,
            SendsTelemetryEvent = json.TryGetProperty("sends_telemetry_event", out var telemetry)
                && telemetry.GetBoolean()
        };
    }

    private static AdvancementDisplay ReadDisplay(JsonElement json)
    {
        var icon = json.GetProperty("icon");
        if (!ItemsRegistry.TryGet(Qualify(icon.GetProperty("id").GetString()!), out var item))
            throw new InvalidDataException("Unknown advancement icon.");
        var flags = json.TryGetProperty("show_toast", out var toast) && !toast.GetBoolean()
            ? (AdvancementFlags)0
            : AdvancementFlags.ShowToast;
        var background = json.TryGetProperty("background", out var texture) ? texture.GetString()! : "";
        if (background.Length > 0)
            flags |= AdvancementFlags.HasBackgroundTexture;

        if (json.TryGetProperty("hidden", out var hidden) && hidden.GetBoolean())
            flags |= AdvancementFlags.Hidden;

        var frame = json.TryGetProperty("frame", out var type) ? type.GetString() : "task";
        return new AdvancementDisplay(Chat(json.GetProperty("title")), Chat(json.GetProperty("description")),
            item, frame switch
            {
                "task" => AdvancementFrameType.Task,
                "goal" => AdvancementFrameType.Goal,
                "challenge" => AdvancementFrameType.Challenge,
                _ => throw new InvalidDataException("Unknown advancement frame.")
            }, flags, background, 0, 0)
        {
            IconStack = new(item, icon.TryGetProperty("count", out var count) ? count.GetInt32() : 1),
            AnnounceToChat = !json.TryGetProperty("announce_to_chat", out var announce) || announce.GetBoolean()
        };
    }

    private static ChatMessage Chat(JsonElement json) => json.ValueKind == JsonValueKind.String
        ? ChatMessage.Simple(json.GetString()!)
        : JsonSerializer.Deserialize<ChatMessage>(json.GetRawText(), Globals.JsonOptions)!;

    private static List<string> Strings(JsonElement json, string name) => json.TryGetProperty(name, out var values)
        ? values.EnumerateArray().Select(value => Qualify(value.GetString()!)).ToList()
        : [];

    internal static string Qualify(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new InvalidDataException("An advancement resource identifier must not be empty.");

        var qualified = identifier.Contains(':') ? identifier : $"minecraft:{identifier}";
        if (!PluginTags.IsFileIdentifier(qualified))
            throw new InvalidDataException($"Invalid resource identifier {identifier}.");

        return qualified;
    }

    private static FrozenDictionary<string, Advancement> ReadVanilla()
    {
        using var stream = typeof(AdvancementsRegistry).Assembly
            .GetManifestResourceStream("Obsidian.Assets.advancements.json")!;
        using var json = JsonDocument.Parse(stream);
        return PlaceDisplays(json.RootElement.EnumerateObject()
            .Select(property => Parse(property.Name, property.Value)));
    }
}
