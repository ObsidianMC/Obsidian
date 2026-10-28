using Obsidian.API;
using Obsidian.API.Inventory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace Obsidian.Tests;

public class RegistryOrdinals
{
    [Fact]
    public void ProtocolIdsMatchVanillaAssets()
    {
        using var registries = Asset("registry_ids.json");
        using var attributes = Asset("attribute_ids.json");
        using var packetTypes = Asset("packet_types.json");

        var tables = new (Type Type, Dictionary<string, int> Entries)[]
        {
            (typeof(EntityType), Registry("minecraft:entity_type")),
            (typeof(ParticleType), Registry("minecraft:particle_type")),
            (typeof(GameEvent), Registry("minecraft:game_event")),
            (typeof(AttributeIds), Values(attributes.RootElement)),
            (typeof(HeightmapType), Values(packetTypes.RootElement.GetProperty("Heightmap.Types").GetProperty("values")))
        };

        foreach (var (type, expected) in tables)
        {
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Static);
            Assert.NotEmpty(expected);
            Assert.All(fields, field => Assert.True(field.IsLiteral, $"{type.Name}.{field.Name} must be a constant."));
            var actual = fields.ToDictionary(field => Normalize(field.Name), field => Assert.IsType<int>(field.GetRawConstantValue()));
            Assert.Equal(expected.OrderBy(entry => entry.Key, StringComparer.Ordinal), actual.OrderBy(entry => entry.Key, StringComparer.Ordinal));
        }

        Dictionary<string, int> Registry(string registry) => registries.RootElement.GetProperty(registry)
            .EnumerateArray()
            .Select((entry, id) => (Name: Normalize(entry.GetString()!), Id: id))
            .ToDictionary(entry => entry.Name, entry => entry.Id);
    }

    private static Dictionary<string, int> Values(JsonElement entries) => entries.EnumerateObject()
        .ToDictionary(entry => Normalize(entry.Name), entry => entry.Value.GetInt32());

    private static string Normalize(string name) => name.Replace("minecraft:", "", StringComparison.Ordinal)
        .Replace("campfire_cozy_smoke", "campfire_cosy_smoke", StringComparison.Ordinal)
        .Replace("_", "", StringComparison.Ordinal)
        .ToUpperInvariant();

    private static JsonDocument Asset(string name)
    {
        using var stream = typeof(Server).Assembly.GetManifestResourceStream($"Obsidian.Assets.{name}")
            ?? throw new InvalidOperationException($"The vanilla asset {name} is missing.");
        return JsonDocument.Parse(stream);
    }
}
