using Obsidian.API;
using Obsidian.Registries;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Obsidian.Tests;

// Temporary: vanilla_worldgen.json was captured on 1.21.11. Match names and every property exactly, independent of
// the current registry's numbering. Phase 5 re-records it on 26.3; remove this helper/table and hash current ids again.
internal static class LegacyWorldgenBlockStates
{
    private static readonly Lazy<FrozenDictionary<int, ushort>> ids = new(Load);

    public static ushort StateId(IBlock block) => ids.Value.TryGetValue(block.GetHashCode(), out var id) ? id
        : throw new InvalidOperationException($"No 1.21.11 block state equivalent for {Key(block.UnlocalizedName, block.GetProperties())} (current id {block.GetHashCode()}).");

    private static string Key(string name, IReadOnlyDictionary<string, string> properties) => properties.Count == 0 ? name
        : $"{name}[{string.Join(',', properties.OrderBy(property => property.Key, StringComparer.Ordinal)
            .Select(property => $"{property.Key}={property.Value}"))}]";

    private static FrozenDictionary<int, ushort> Load()
    {
        using var legacyStream = typeof(LegacyWorldgenBlockStates).Assembly
            .GetManifestResourceStream("Obsidian.Tests.Assets.legacy_block_states.json")
            ?? throw new InvalidOperationException("Missing legacy worldgen block state table.");
        var legacyStates = JsonSerializer.Deserialize<string[]>(legacyStream)!;
        var legacyIds = legacyStates.Select((key, id) => (key, id: checked((ushort)id)))
            .ToDictionary(state => state.key, state => state.id, StringComparer.Ordinal);

        using var currentStream = typeof(BlocksRegistry).Assembly.GetManifestResourceStream("Obsidian.Assets.blocks.json")
            ?? throw new InvalidOperationException("Missing current blocks report.");
        using var current = JsonDocument.Parse(currentStream);
        var mapped = new Dictionary<int, ushort>();
        foreach (var block in current.RootElement.EnumerateObject())
        {
            foreach (var state in block.Value.GetProperty("states").EnumerateArray())
            {
                var properties = state.TryGetProperty("properties", out var values)
                    ? values.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.GetString()!)
                    : new Dictionary<string, string>();
                if (legacyIds.TryGetValue(Key(block.Name, properties), out var legacyId))
                    mapped.Add(state.GetProperty("id").GetInt32(), legacyId);
            }
        }

        return mapped.ToFrozenDictionary();
    }
}
