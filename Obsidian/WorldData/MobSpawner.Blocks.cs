using Obsidian.Entities;
using Obsidian.Entities.AI;
using Obsidian.Entities.Factories;
using Obsidian.Nbt;

namespace Obsidian.WorldData;

internal sealed partial class MobSpawner
{
    private void TickSpawner(DataBlockEntity spawner, IPlayer[] players)
    {
        var position = (VectorF)spawner.BlockPosition + 0.5f;
        var data = spawner.Data;
        var playerRange = ReadShort(data, "RequiredPlayerRange", 16, 1, 128);
        if (!players.Any(player => player.Health > 0 && (player.Position - position).MagnitudeSquared() < playerRange * playerRange))
            return;
        var delay = ReadShort(data, "Delay", 20, -1, short.MaxValue);
        if (delay < 0) { ResetSpawner(spawner); return; }
        if (delay > 0) { spawner.Set("Delay", (short)(delay - 1)); return; }
        if (!data.TryGetTag<NbtCompound>("SpawnData", out var spawnData) || !spawnData.TryGetTag<NbtCompound>("entity", out var entityData) ||
            !entityData.TryGetTagValue<string>("id", out var id) || !EntityNbt.TryParseType(id, out var type))
            return;
        var range = ReadShort(data, "SpawnRange", 4, 1, 16);
        var count = ReadShort(data, "SpawnCount", 4, 1, 32);
        var maximum = ReadShort(data, "MaxNearbyEntities", 6, 1, 128);
        var spawned = false;
        var terrain = new MobTerrain(level);
        for (var attempt = 0; attempt < count; attempt++)
        {
            if (level.GetEntitiesInRange(position, range * 2 + 2).Count(entity => entity.Type == type) >= maximum)
            { ResetSpawner(spawner); return; }
            var point = position + new VectorF((random.NextSingle() - random.NextSingle()) * range,
                random.Next(-1, 2) - 0.5f, (random.NextSingle() - random.NextSingle()) * range);
            var blockPoint = (Vector)point.Floor();
            if (level.IsOutsideBuildHeight(blockPoint.Y))
                continue;
            if (level.GetLoadedChunk(blockPoint.X >> 4, blockPoint.Z >> 4) is not { } chunk)
                continue;
            var mob = EntitySpawner.CreateMob(level, type);
            if (mob == null || level.LevelData.Difficulty == Difficulty.Peaceful &&
                (mob.Hostile || mob is Zombie or Skeleton or Creeper))
                continue;
            if (terrain.GetBlock(blockPoint) is not { } feet || (IsAquatic(type) ? feet.Material != Material.Water : feet.IsLiquid) ||
                !terrain.IsFree(mob.Dimension.CreateBBFromPosition(point)))
                continue;
            var blockLight = chunk.GetLightLevel(blockPoint.X, blockPoint.Y, blockPoint.Z, LightType.Block);
            var sky = chunk.GetLightLevel(blockPoint.X, blockPoint.Y, blockPoint.Z, LightType.Sky);
            var maxBlockLight = type == EntityType.Blaze || level.DimensionName == "minecraft:the_nether" ? 11 : 0;
            if ((mob.Hostile || mob is Zombie or Skeleton or Creeper) && type is not EntityType.MagmaCube and not EntityType.Silverfish && (blockLight > maxBlockLight ||
                type != EntityType.Blaze && level.DimensionName != "minecraft:the_nether" &&
                Math.Max(blockLight, Math.Max(0, sky - (level.DayTime is >= 12000 and < 23000 ? 11 : 0))) > random.Next(8)))
                continue;
            mob.EntityId = Server.GetNextEntityId();
            mob.Position = point;
            mob.Yaw = random.Next(360);
            mob.InitializeAi();
            if (entityData.Count > 1)
            {
                var saved = EntityNbt.Save(mob)!;
                foreach (var (name, value) in entityData)
                    if (name is not ("id" or "Pos" or "UUID")) { saved.Remove(name); saved.Add(name, value); }
                mob.ReadSave(saved);
            }
            if (!terrain.IsFree(mob.Dimension.CreateBBFromPosition(point)) || level.GetEntitiesInRange(point, 2)
                .Any(entity => MobTerrain.Overlaps(mob.Dimension.CreateBBFromPosition(point), entity.Dimension.CreateBBFromPosition(entity.Position))))
                continue;
            level.SpawnEntity(mob);
            level.BroadcastLevelEvent(2004, spawner.BlockPosition, 0);
            spawned = true;
        }
        if (spawned) ResetSpawner(spawner);
    }
    private void ResetSpawner(DataBlockEntity spawner)
    {
        var minimum = ReadShort(spawner.Data, "MinSpawnDelay", 200, 1, short.MaxValue);
        var maximum = ReadShort(spawner.Data, "MaxSpawnDelay", 800, minimum, short.MaxValue);
        spawner.Set("Delay", (short)(maximum <= minimum ? minimum : random.Next(minimum, maximum)));
        if (!spawner.Data.TryGetTag<NbtList>("SpawnPotentials", out var potentials)) return;
        var entries = potentials.OfType<NbtCompound>().Where(entry => entry.TryGetTagValue<int>("weight", out var weight) && weight > 0 &&
            entry.TryGetTag<NbtCompound>("data", out _)).ToArray();
        var total = entries.Sum(entry => (long)entry.GetInt("weight"));
        if (total <= 0) return;
        var selected = random.NextInt64(total);
        foreach (var entry in entries)
        {
            selected -= entry.GetInt("weight");
            if (selected < 0)
            {
                var spawnData = new NbtCompound("SpawnData");
                entry.TryGetTag<NbtCompound>("data", out var selectedData);
                foreach (var (name, value) in selectedData!) spawnData.Add(name, value);
                spawner.Set(spawnData);
                break;
            }
        }
    }
    private static int ReadShort(NbtCompound tag, string name, int fallback, int minimum, int maximum) =>
        Math.Clamp(tag.TryGetTagValue<short>(name, out var value) ? value : fallback, minimum, maximum);
}
