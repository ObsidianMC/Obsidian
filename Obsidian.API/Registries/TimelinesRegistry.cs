using System.Collections.Frozen;
using System.Collections.Immutable;

namespace Obsidian.API.Registries;

/// <summary>
/// Vanilla's timelines (the <c>minecraft:timeline</c> registry), numbered by identifier like a data pack's registry,
/// and the tags dimension types name: clients take a dimension's day and night, its moon and its villager schedule
/// from the timelines its tag holds, so without them the overworld's sky never darkens.
/// </summary>
public static class TimelinesRegistry
{
    public const string CodecKey = "minecraft:timeline";

    /// <summary>
    /// The timelines' identifiers, by id.
    /// </summary>
    public static ImmutableArray<string> Identifiers { get; } =
        ["minecraft:day", "minecraft:early_game", "minecraft:moon", "minecraft:villager_schedule"];

    private static readonly FrozenDictionary<string, int> ids =
        Identifiers.Select((name, id) => (name, id)).ToFrozenDictionary(pair => pair.name, pair => pair.id);

    static TimelinesRegistry()
    {
        // The generated tags have no entries, since the registry isn't among the generated ones. Their values, from
        // vanilla's tags, with #minecraft:universal resolved.
        ImmutableArray<int> universal = [IdOf("minecraft:villager_schedule")];

        TagsRegistry.Timeline.Universal.SetEntries(universal);
        TagsRegistry.Timeline.InEnd.SetEntries(universal);
        TagsRegistry.Timeline.InNether.SetEntries(universal);
        TagsRegistry.Timeline.InOverworld.SetEntries(
        [
            .. universal, IdOf("minecraft:day"), IdOf("minecraft:moon"), IdOf("minecraft:early_game")
        ]);
    }

    /// <summary>
    /// The timeline's id, or -1 for an unknown identifier.
    /// </summary>
    public static int IdOf(string identifier) => ids.GetValueOrDefault(identifier, -1);
}
