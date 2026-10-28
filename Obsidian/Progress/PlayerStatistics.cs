using Obsidian.API.Progress;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;
using System.Text.Json.Nodes;
using System.Threading;

namespace Obsidian.Progress;

/// <summary>Vanilla's grouped stats JSON, retaining unknown entries for temporarily absent plugin content.</summary>
internal sealed class PlayerStatistics : IStatistics
{
    private readonly Lock gate = new();
    private JsonObject values = new();

    private static readonly string[] types =
    [
        "minecraft:mined", "minecraft:crafted", "minecraft:used", "minecraft:broken", "minecraft:picked_up",
        "minecraft:dropped", "minecraft:killed", "minecraft:killed_by", "minecraft:custom"
    ];

    public int Get(string identifier, string type = "minecraft:custom")
    {
        lock (this.gate)
            return this.values[type]?[identifier]?.GetValue<int>() ?? 0;
    }

    public void Increment(string identifier, int amount = 1, string type = "minecraft:custom")
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        if (Resolve(type, identifier, requireActive: true) < 0)
            throw new ArgumentException($"Unknown statistic {type}/{identifier}.");

        lock (this.gate)
        {
            var group = this.values[type] as JsonObject;
            if (group is null)
            {
                group = new JsonObject();
                this.values[type] = group;
            }

            var previous = group[identifier]?.GetValue<int>() ?? 0;
            group[identifier] = (int)Math.Clamp((long)previous + amount, 0, int.MaxValue);
        }
    }

    internal void Reset(string identifier)
    {
        lock (this.gate)
        {
            if (this.values["minecraft:custom"] is JsonObject custom)
                custom[identifier] = 0;
        }
    }

    public void Load(JsonObject json)
    {
        lock (this.gate)
            this.values = json["stats"]?.DeepClone() as JsonObject ?? new JsonObject();
    }

    public JsonObject Save()
    {
        lock (this.gate)
        {
            return new JsonObject
            {
                ["stats"] = this.values.DeepClone(),
                ["DataVersion"] = ServerConstants.DataVersion
            };
        }
    }

    public AwardStatsPacket CreatePacket()
    {
        var packet = new AwardStatsPacket();
        lock (this.gate)
        {
            foreach (var (type, group) in this.values)
            {
                var category = Array.IndexOf(types, type);
                if (category < 0 || group is not JsonObject entries)
                    continue;

                foreach (var (identifier, value) in entries)
                {
                    var id = Resolve(type, identifier);
                    if (id >= 0 && value is JsonValue number && number.TryGetValue<int>(out var count))
                        packet.Add(new Statistic((CategoryIds)category, id, count));
                }
            }
        }

        return packet;
    }

    private static int Resolve(string type, string identifier, bool requireActive = false)
    {
        if (type == "minecraft:custom")
            return CustomStatisticsRegistry.GetId(identifier, requireActive);

        var registry = type switch
        {
            "minecraft:mined" => "minecraft:block",
            "minecraft:crafted" or "minecraft:used" or "minecraft:broken" or "minecraft:picked_up"
                or "minecraft:dropped" => "minecraft:item",
            "minecraft:killed" or "minecraft:killed_by" => "minecraft:entity_type",
            _ => null
        };
        if (registry is null)
            return -1;

        if (registry == "minecraft:item")
            return ItemsRegistry.All.FirstOrDefault(item => item.UnlocalizedName == identifier)?.Id ?? -1;

        if (registry == "minecraft:block" && BlocksRegistry.TryGetPluginBlock(identifier, out var pluginBlock))
            return pluginBlock.Id;

        if (registry == "minecraft:entity_type" && EntityDefinitionsRegistry.TryGet(identifier, out var entity))
            return entity.Id;

        var names = VanillaRegistryIds.Get(registry);
        for (var id = 0; id < names.Count; id++)
        {
            if (names[id] == identifier)
                return id;
        }

        return -1;
    }
}
