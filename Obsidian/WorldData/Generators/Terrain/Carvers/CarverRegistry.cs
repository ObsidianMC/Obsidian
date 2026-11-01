using Microsoft.Extensions.Logging;
using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.Plugins;
using Obsidian.Registries.Codecs;
using Obsidian.WorldData.Generators.Density;
using System.Collections.Frozen;
using System.IO;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Terrain.Carvers;

/// <summary>
/// One reload's 26.3 carvers and resolved biome lists. Only host-owned configurations and plugin containers are
/// retained; no plugin type, reader or assembly belongs in a carver cache.
/// </summary>
internal sealed partial class CarverRegistry
{
    private readonly Dictionary<string, CarverConfiguration> configurations = [];
    private readonly Dictionary<string, ImmutableArray<PluginContainer>> owners = [];
    private readonly Dictionary<string, string[]> biomes = [];
    private readonly Dictionary<string, HashSet<PluginContainer>> biomeOwners = [];
    private int inlineIds;

    internal HashSet<FeatureFile> ValidModifiers { get; } = [];

    internal FrozenSet<int>? Uncarvable { get; private init; }

    public CarverConfiguration Configuration(string id) => this.configurations[WorldgenData.Qualify(id)];

    /// <summary>The ordered list, including duplicates, which determines each carver's random seed.</summary>
    public string[] BiomeCarvers(string biome) => this.biomes.GetValueOrDefault(biome, []);

    public ImmutableArray<PluginContainer> OwnersOf(IEnumerable<string> possibleBiomes)
    {
        if (this.owners.Count == 0 && this.biomeOwners.Count == 0)
            return [];

        return [.. possibleBiomes.SelectMany(biome => this.BiomeCarvers(biome)
            .SelectMany(id => this.owners.GetValueOrDefault(id, []))
            .Concat(this.biomeOwners.GetValueOrDefault(biome) ?? [])).Distinct()];
    }

    public static CarverRegistry Build(
        IEnumerable<FeatureFile> files,
        IEnumerable<CodecFile> biomeFiles,
        WorldgenData data,
        ILogger logger,
        Action<BiomeModifierData>? validateFeatures = null,
        bool usePluginBlockTags = false)
    {
        var result = new CarverRegistry
        {
            Uncarvable = usePluginBlockTags && TagsRegistry.TryGet("block", "minecraft:uncarvable", out var tag)
                ? tag.Entries.ToFrozenSet()
                : null
        };
        var kept = VanillaOverrides.LastWins(files, file => (file.Registry, file.Identifier),
            file => file.Source, logger);

        foreach (var id in data.Ids("carver"))
        {
            result.configurations[id] = Read(data.Read("carver", id));
        }

        foreach (var file in kept.Where(file => file.Registry == PluginFeatures.ConfiguredCarver))
        {
            try
            {
                result.configurations[file.Identifier] = Read(file.Json);
                result.owners[file.Identifier] = file.Owner is PluginContainer owner ? [owner] : [];
            }
            catch (InvalidDataException ex)
            {
                Log.Invalid(logger, file.Source, ex.Message);
            }
        }

        var suppliedBiomes = PluginCodecs.Winners(biomeFiles, logger)
            .Where(file => file.Registry == PluginCodecs.Biome)
            .ToDictionary(file => file.Identifier);

        foreach (var biome in data.Ids("biome").Concat(suppliedBiomes.Keys).Distinct())
        {
            var file = suppliedBiomes.GetValueOrDefault(biome);

            try
            {
                if (file is not null)
                {
                    var element = (BiomeElement)file.Element;
                    result.biomes[biome] = result.Resolve(element.Carvers, file.Owner);
                }
                else
                {
                    var json = data.Read("biome", biome);
                    result.biomes[biome] = json.TryGetProperty("carvers", out var carvers)
                        ? result.Resolve(Entries(carvers), null)
                        : [];
                }
            }
            catch (InvalidDataException ex)
            {
                Log.Invalid(logger, file?.Source ?? biome, ex.Message);
                // A failed plugin biome list leaves vanilla's list in place, or none for a new biome.
                result.biomes[biome] = WorldgenData.Vanilla.Ids("biome").Contains(biome)
                    ? result.Resolve(Entries(WorldgenData.Vanilla.Read("biome", biome).GetProperty("carvers")), null)
                    : [];
            }
        }

        var modifiers = new List<(FeatureFile File, List<string> Biomes, string[] Add, string[] Remove)>();

        foreach (var file in kept.Where(file => file.Modifier is not null))
        {
            try
            {
                var modifier = file.Modifier!;
                var biomes = PluginFeatures.ResolveBiomes(modifier);
                var add = result.Resolve(modifier.Add.Carvers, file.Owner);
                var remove = result.Resolve(modifier.Remove.Carvers, file.Owner);
                validateFeatures?.Invoke(modifier);
                modifiers.Add((file, biomes, add, remove));
                result.ValidModifiers.Add(file);
            }
            catch (InvalidDataException ex)
            {
                Log.Invalid(logger, file.Source, ex.Message);
            }
        }

        // Match the shared modifier lifecycle: all additions, then all removals, in load order.
        foreach (var (file, biomes, add, _) in modifiers)
        {
            foreach (var biome in biomes)
            {
                result.biomes[biome] = [.. result.BiomeCarvers(biome), .. add];
                result.NoteModifierOwner(biome, file);
            }
        }

        foreach (var (file, biomes, _, remove) in modifiers)
        {
            var removed = remove.ToHashSet();

            foreach (var biome in biomes)
            {
                result.biomes[biome] = [.. result.BiomeCarvers(biome).Where(id => !removed.Contains(id))];
                result.NoteModifierOwner(biome, file);
            }
        }

        return result;
    }

    private void NoteModifierOwner(string biome, FeatureFile file)
    {
        if (file.Owner is not PluginContainer owner
            || (file.Modifier!.Add.Carvers.IsEmpty && file.Modifier.Remove.Carvers.IsEmpty))
        {
            return;
        }

        if (!this.biomeOwners.TryGetValue(biome, out var owners))
            this.biomeOwners[biome] = owners = [];

        owners.Add(owner);
    }

    private string[] Resolve(IEnumerable<string> entries, PluginContainer? owner)
    {
        var resolved = new List<string>();

        foreach (var entry in entries)
        {
            if (entry.StartsWith('#'))
            {
                // PluginTags has already expanded nested tags and rejected required dependency cycles.
                if (!PluginTags.TryGetIdentifiers(PluginTags.ConfiguredCarver, entry[1..], out var ids))
                    throw new InvalidDataException($"{entry} isn't a configured carver tag.");

                foreach (var id in ids)
                {
                    resolved.Add(this.Find(id));
                }
            }
            else if (entry.StartsWith('{'))
            {
                using var document = JsonDocument.Parse(entry, new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });
                var configuration = Read(document.RootElement);
                string id;

                do
                {
                    id = $"obsidian:inline_carver/{this.inlineIds++}";
                }
                while (this.configurations.ContainsKey(id));

                this.configurations[id] = configuration;
                this.owners[id] = owner is null ? [] : [owner];
                resolved.Add(id);
            }
            else
            {
                resolved.Add(this.Find(entry));
            }
        }

        return [.. resolved];
    }

    private string Find(string id)
    {
        id = WorldgenData.Qualify(id);
        return this.configurations.ContainsKey(id)
            ? id
            : throw new InvalidDataException($"{id} isn't a configured carver.");
    }

    private static IEnumerable<string> Entries(JsonElement json) => json.ValueKind == JsonValueKind.Array
        ? json.EnumerateArray().Select(Entry)
        : [Entry(json)];

    private static string Entry(JsonElement json) =>
        json.ValueKind == JsonValueKind.Object ? json.GetRawText() : json.GetString()!;

    private static CarverConfiguration Read(JsonElement json)
    {
        try
        {
            return CarverConfiguration.Read(json);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException
            or FormatException or NotSupportedException or OverflowException or ArgumentException)
        {
            throw new InvalidDataException(ex.Message, ex);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "Left out {Source}: {Message}")]
        public static partial void Invalid(ILogger logger, string source, string message);
    }
}
