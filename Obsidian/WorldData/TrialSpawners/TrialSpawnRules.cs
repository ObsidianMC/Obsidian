using Obsidian.Entities;
using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Serverbound;

namespace Obsidian.WorldData.TrialSpawners;

/// <summary>
/// Where a trial spawner's mob may spawn, beyond fitting and being seen: vanilla's spawn rules for the
/// <c>TRIAL_SPAWNER</c> reason, a spawn data's light limits, and nothing in the way.
/// </summary>
internal static class TrialSpawnRules
{
    /// <summary>
    /// Vanilla's <c>SpawnPlacements.checkSpawnRules</c> with the <c>TRIAL_SPAWNER</c> reason. Every monster's rule
    /// lets a trial spawner's mob spawn anywhere, in any light, but monsters never spawn in peaceful. Other mobs keep
    /// the floor rules spawners use.
    /// </summary>
    /// <param name="sample">A new entity of the type, as the type makes it.</param>
    public static bool Allow(AbstractLevel level, Entity sample, Vector position)
    {
        if (sample is not Mob mob)
            return true;

        if (MobSpawner.SpawnCategory(mob) == "monster")
            return level.LevelData.Difficulty != Difficulty.Peaceful;

        var chunk = level.GetLoadedChunk(position.X >> 4, position.Z >> 4);
        if (sample.Type is not EntityType type || chunk is null)
            return true;

        var feet = level.BlockUpdates.GetBlock(position);
        var floor = level.BlockUpdates.GetBlock(position + Vector.Down);
        var biome = chunk.GetBiome(position.X, position.Y, position.Z);
        return MobSpawner.FitsVanillaPlacement(type, feet, floor, biome);
    }

    /// <summary>
    /// A spawn data's <c>custom_spawn_rules</c>, like vanilla's <c>CustomSpawnRules.isValidPosition</c>: the block and
    /// sky light at the position within their limits (each a number or a range, by default 0 to 15).
    /// </summary>
    public static bool FitsLightLimits(AbstractLevel level, Vector position, NbtCompound rules)
    {
        if (level.GetLoadedChunk(position.X >> 4, position.Z >> 4) is not { } chunk)
            return false;

        var block = chunk.GetLightLevel(position.X, position.Y, position.Z, LightType.Block);
        var sky = chunk.GetLightLevel(position.X, position.Y, position.Z, LightType.Sky);
        return InRange(rules, "block_light_limit", block) && InRange(rules, "sky_light_limit", sky);
    }

    private static bool InRange(NbtCompound rules, string name, int light)
    {
        if (!rules.TryGetTag(name, out var limit))
            return light is >= 0 and <= 15;

        if (limit.TryGetNumber<int>(out var exact))
            return light == exact;

        if (limit is not NbtCompound range)
            return true;

        var min = range.TryGetNumber<int>("min_inclusive", out var low) ? low : 0;
        var max = range.TryGetNumber<int>("max_inclusive", out var high) ? high : 15;
        return light >= min && light <= max;
    }

    /// <summary>
    /// Vanilla's <c>Mob.checkSpawnObstruction</c>: no liquid in the mob's box, and no entity that blocks building in
    /// it either.
    /// </summary>
    public static bool IsUnobstructed(AbstractLevel level, Mob mob)
    {
        var bounds = mob.Dimension.CreateBBFromPosition(mob.Position);
        if (!new MobTerrain(level).IsFreeOfFluid(bounds))
            return false;

        var reach = (float)Math.Max(bounds.Max.Y - bounds.Min.Y, bounds.Max.X - bounds.Min.X) + 2;
        foreach (var entity in level.GetEntitiesInRange(mob.Position, reach))
        {
            if (!UseItemOnPacket.BlocksBuilding(entity))
                continue;

            var other = entity.Dimension.CreateBBFromPosition(entity.Position);
            if (MobTerrain.Overlaps(bounds, other))
                return false;
        }

        return true;
    }
}
