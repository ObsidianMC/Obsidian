using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.Registry.Codecs.Dimensions;
using Obsidian.WorldData;
using System.Runtime.CompilerServices;

namespace Obsidian.Registries;

internal sealed partial record BiomeSpawns
{
    private readonly ConditionalWeakTable<DimensionElement, ConditionalWeakTable<BiomeElement, SpawnSettings>>
        environments = new();

    internal Func<string, SpawnSettings, SpawnSettings>? EnvironmentModifiers { get; init; }

    /// <summary>
    /// Spawns at a position's dimension and biome, with plugin additions/removals applied after attributes.
    /// A rebuilt plugin snapshot owns a fresh cache, so unloads and reloads cannot retain stale modifier results.
    /// </summary>
    public SpawnSettings At(DimensionCodec dimension, BiomeCodec biome) =>
        this.environments.GetValue(dimension.Element, _ => new()).GetValue(biome.Element, _ =>
        {
            var settings = SpawnEnvironment.Resolve(dimension, biome);
            return this.EnvironmentModifiers?.Invoke(biome.Name, settings) ?? settings;
        });
}
