using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.Registries;
using Obsidian.WorldData;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// A spawner's mob uses vanilla's placement predicate (BaseSpawner.serverTick: SpawnPlacements.isSpawnPositionOk).
/// </summary>
public sealed class SpawnerPlacement
{
    [Fact(DisplayName = "Spawner mobs follow vanilla placement: no passive mob over water, animals on grass only")]
    public void SpawnerPlacementFollowsVanilla()
    {
        var air = BlocksRegistry.Air;
        var water = BlocksRegistry.Water;
        var grass = BlocksRegistry.GrassBlock;
        var stone = BlocksRegistry.Stone;
        BiomeCodec? biome = null;

        // A cow standing on a lake: water below its feet is rejected, the surface of the water too.
        Assert.False(MobSpawner.FitsVanillaPlacement(API.EntityType.Cow, air, water, biome!));
        Assert.False(MobSpawner.FitsVanillaPlacement(API.EntityType.Cow, water, grass, biome!));

        // Animals need grass; a cow on stone is not placed.
        Assert.True(MobSpawner.FitsVanillaPlacement(API.EntityType.Cow, air, grass, biome!));
        Assert.False(MobSpawner.FitsVanillaPlacement(API.EntityType.Cow, air, stone, biome!));

        // Aquatic mobs need water at the feet.
        Assert.True(MobSpawner.FitsVanillaPlacement(API.EntityType.Squid, water, water, biome!));
        Assert.False(MobSpawner.FitsVanillaPlacement(API.EntityType.Squid, air, water, biome!));

        // Hostile mobs keep their own rules: a zombie on stone is not rejected by the animal floor.
        Assert.True(MobSpawner.FitsVanillaPlacement(API.EntityType.Zombie, air, stone, biome!));
    }
}
