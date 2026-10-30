using Obsidian.API.World.Features;
using Obsidian.WorldData.Features;
using Obsidian.Nbt;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features;

internal sealed partial class FeatureRegistry
{
    // These algorithms remain shared with the live generator. Their 26.3 configs are compiled here, without
    // resolving legacy configured-feature or placed-feature registries; 26.3 captures verify each adapter.
    private static Feature SharedAlgorithm(ConfiguredFeatureBase feature) => (context, random, origin) =>
        feature.Place(new FeatureContext
        {
            Level = context.Level,
            Random = random,
            Origin = origin,
            Generation = new WorldGenerationContext(context.GeneratorMinY, context.GeneratorHeight)
        });

    private static Feature Bamboo(JsonElement json) => SharedAlgorithm(new BambooFeature
    {
        Probability = json.GetProperty("probability").GetSingle()
    });

    private static Feature BlueIce() => SharedAlgorithm(new BlueIceFeature());

    private static Feature BonusChest() => SharedAlgorithm(new BonusChestFeature());

    private static readonly (string Name, short Value)[] spawnerDefaults =
    [
        ("Delay", 20),
        ("MinSpawnDelay", 200),
        ("MaxSpawnDelay", 800),
        ("SpawnCount", 4),
        ("MaxNearbyEntities", 6),
        ("RequiredPlayerRange", 16),
        ("SpawnRange", 4)
    ];

    private static Feature MonsterRoom()
    {
        var place = SharedAlgorithm(new MonsterRoomFeature());

        return (context, random, origin) =>
        {
            var placed = place(context, random, origin);

            if (placed && context.Level.GetBlockEntity(origin) is DataBlockEntity spawner
                && spawner.Id == "minecraft:mob_spawner")
            {
                // BaseSpawner.save writes these defaults even before its first tick. Keep this in the 26.3
                // adapter: the legacy generator's captures intentionally retain its earlier sparse data.
                foreach (var (name, value) in spawnerDefaults)
                {
                    if (!spawner.Data.HasTag(name))
                    {
                        spawner.Set(name, value);
                    }
                }

                if (!spawner.Data.HasTag("SpawnPotentials"))
                {
                    spawner.Set(new NbtList(NbtTagType.Compound, "SpawnPotentials"));
                }
            }

            return placed;
        };
    }

    private static Feature Vines() => SharedAlgorithm(new VinesFeature());

    private static Feature VoidStartPlatform() => SharedAlgorithm(new VoidStartPlatformFeature());

    private static Feature EndSpike(JsonElement json)
    {
        var reader = new WorldgenReader(
            WorldgenTypes.BuiltIn,
            _ => throw new InvalidOperationException("End spikes cannot resolve configured features."),
            _ => throw new InvalidOperationException("End spikes cannot resolve placed features."));
        var spikes = reader.Read<ImmutableArray<Obsidian.WorldData.Features.EndSpike>>(json.GetProperty("spikes"));
        Vector? beam = json.TryGetProperty("crystal_beam_target", out var target)
            ? new Vector(target[0].GetInt32(), target[1].GetInt32(), target[2].GetInt32())
            : null;

        return SharedAlgorithm(new EndSpikeFeature
        {
            Spikes = spikes,
            CrystalBeamTarget = beam,
            CrystalInvulnerable = json.TryGetProperty("crystal_invulnerable", out var value) && value.GetBoolean()
        });
    }
}
