using System.Text.Json.Nodes;
using static Obsidian.AssetGenerator.Datagen.DatagenJson;

namespace Obsidian.AssetGenerator.Datagen;

/// <summary>
/// Writes <c>villager_trades.json</c>: the villager trades and trade sets (26.1+), with their tags expanded.
/// </summary>
/// <remarks>
/// <c>villager_trade</c> isn't a registry clients know, so the server reads its tags only here, never syncing them.
/// A trade set's <c>trades</c> becomes the list of trade ids in vanilla's tag order, which the offer draws index into.
/// Enchantment and potion <c>options</c> and <c>double_trade_price_enchantments</c> become lists of ids too, which
/// the runtime modifier reader expects (like loot's).
/// </remarks>
internal static class TradeAssets
{
    public static void Write(string data, string outputDirectory)
    {
        var tags = Path.Combine(data, "tags");

        var trades = Combine(Path.Combine(data, "villager_trade"), DatagenAssets.ToId);
        foreach (var (_, trade) in trades)
        {
            ExpandTrade(trade!.AsObject(), tags);
        }

        var tradeSets = Combine(Path.Combine(data, "trade_set"), DatagenAssets.ToId);
        foreach (var (_, tradeSet) in tradeSets)
        {
            var set = tradeSet!.AsObject();
            set["trades"] = Expand(set["trades"]!, Path.Combine(tags, "villager_trade"));
        }

        var assets = new JsonObject
        {
            ["trades"] = trades,
            ["trade_sets"] = tradeSets
        };
        WriteJson(Path.Combine(outputDirectory, "villager_trades.json"), assets);
    }

    private static void ExpandTrade(JsonObject trade, string tags)
    {
        if (trade["double_trade_price_enchantments"] is JsonNode enchantments)
            trade["double_trade_price_enchantments"] = Expand(enchantments, Path.Combine(tags, "enchantment"));

        if (trade["given_item_modifier"] is JsonNode modifier)
            ExpandOptions(modifier, tags);
    }

    // The options of enchanting and potion modifiers, nested ones (on_pass/on_fail) included.
    private static void ExpandOptions(JsonNode node, string tags)
    {
        if (node is JsonArray array)
        {
            foreach (var element in array)
            {
                ExpandOptions(element!, tags);
            }

            return;
        }

        if (node is not JsonObject modifier)
            return;

        var type = modifier["type"]?.GetValue<string>();
        var registry = type == "minecraft:set_random_potion" ? "potion" : "enchantment";
        if (modifier["options"] is JsonNode options)
            modifier["options"] = Expand(options, Path.Combine(tags, registry));

        foreach (var (name, value) in modifier.ToList())
        {
            if (name != "options" && value is not null)
                ExpandOptions(value, tags);
        }
    }

    /// <summary>
    /// A holder set (an id, a list of ids or a <c>#tag</c>) as a list of ids, in vanilla's tag order: the declaration
    /// order, with a nested tag's entries in its place and duplicates dropped.
    /// </summary>
    private static JsonArray Expand(JsonNode set, string tagDirectory)
    {
        var ids = new List<string>();
        if (set is JsonArray values)
        {
            foreach (var value in values)
            {
                ids.Add(value!.GetValue<string>());
            }
        }
        else
        {
            var value = set.GetValue<string>();
            if (value.StartsWith('#'))
                AddTag(value[1..], tagDirectory, ids, []);
            else
                ids.Add(value);
        }

        return [.. ids.Distinct().Select(id => (JsonNode)id)];
    }

    private static void AddTag(string tag, string tagDirectory, List<string> ids, HashSet<string> resolving)
    {
        if (!resolving.Add(tag))
            throw new InvalidDataException($"Tag #{tag} includes itself.");

        var path = Path.Combine(tagDirectory, tag[(tag.IndexOf(':') + 1)..] + ".json");
        foreach (var entry in ReadObject(path)["values"]!.AsArray())
        {
            // An entry is an id or #tag, or an object with one as its "id".
            var id = entry is JsonObject reference
                ? reference["id"]!.GetValue<string>()
                : entry!.GetValue<string>();
            if (id.StartsWith('#'))
                AddTag(id[1..], tagDirectory, ids, resolving);
            else
                ids.Add(id);
        }

        resolving.Remove(tag);
    }
}
