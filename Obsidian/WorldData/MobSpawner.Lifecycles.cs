using Obsidian.Entities;
using Obsidian.Entities.AI;
using Obsidian.Nbt;

namespace Obsidian.WorldData;

internal sealed partial class MobSpawner
{
    private void TickHive(DataBlockEntity hive)
    {
        var data = hive.SnapshotData();
        if (!data.TryGetTag<NbtList>("bees", out var occupants)) return;
        var terrain = new MobTerrain(level);
        var block = terrain.GetBlock(hive.BlockPosition);
        if (block?.Material is not Material.BeeNest and not Material.Beehive) return;
        var facing = block.GetProperty("facing") switch
        {
            "north" => new Vector(0, 0, -1), "east" => new Vector(1, 0, 0), "west" => new Vector(-1, 0, 0), _ => new Vector(0, 0, 1)
        };
        var position = (VectorD)(hive.BlockPosition + facing) + new VectorD(0.5f, 0.2f, 0.5f);
        var retained = new NbtList(NbtTagType.Compound, "bees");
        foreach (var occupant in occupants.OfType<NbtCompound>())
        {
            var elapsed = occupant.GetInt("ticks_in_hive") + 1;
            occupant.Set(new NbtTag<int>("ticks_in_hive", elapsed));
            if (elapsed < Math.Max(600, occupant.GetInt("min_ticks_in_hive")) || level.LevelData.Raining || level.DayTime is >= 12000 and < 23000 ||
                !occupant.TryGetTag<NbtCompound>("entity_data", out var saved))
            { retained.Add(occupant); continue; }
            saved.Set(EntityNbt.DoubleList("Pos", position));
            if (EntityNbt.Load(saved, level) is not Bee bee || !terrain.IsFree(bee.Dimension.CreateBBFromPosition(position)))
            { retained.Add(occupant); continue; }
            bee.HivePosition = hive.BlockPosition;
            if (bee.HasNectar)
            {
                bee.HasNectar = false;
                var honey = int.TryParse(block.GetProperty("honey_level"), out var value) ? value : 0;
                block = block.WithProperty("honey_level", Math.Min(5, honey + (random.Next(100) == 0 ? 2 : 1)));
                var updated = block;
                level.EnqueueEntityAction(() => level.SetBlockAsync(hive.BlockPosition, updated, true));
            }
            level.SpawnEntity(bee);
        }
        hive.Set(retained);
    }

    private void TickFrogspawn(Chunk chunk)
    {
        var terrain = new MobTerrain(level);
        foreach (var (position, delay) in chunk.FrogspawnTicks.ToArray())
        {
            if (terrain.GetBlock(position)?.Material != Material.Frogspawn)
            { chunk.FrogspawnTicks.TryRemove(position, out _); continue; }
            if (delay > 1) { chunk.FrogspawnTicks[position] = delay - 1; continue; }
            chunk.FrogspawnTicks.TryRemove(position, out _);
            level.EnqueueEntityAction(async () =>
            {
                if (terrain.GetBlock(position)?.Material != Material.Frogspawn) return;
                await level.SetBlockAsync(position, BlocksRegistry.Air, true);
                if (terrain.GetBlock(new Vector(position.X, position.Y - 1, position.Z))?.Material != Material.Water) return;
                for (var index = random.Next(2, 6); index > 0; index--)
                    level.SpawnEntity(new Tadpole { Level = level, EntityId = Server.GetNextEntityId(),
                        Position = new VectorD(position.X + 0.2f + random.NextSingle() * 0.6f, position.Y - 0.5f, position.Z + 0.2f + random.NextSingle() * 0.6f),
                        PersistenceRequired = true });
            });
        }
    }

    private void TickThunder(IChunk chunk)
    {
        if (level.DimensionName != "minecraft:overworld" || !level.LevelData.Thundering || !level.LevelData.Raining || random.Next(100000) != 0) return;
        var x = chunk.X * 16 + random.Next(16);
        var z = chunk.Z * 16 + random.Next(16);
        var y = GetSpawnHeight(chunk, x, z);
        var terrain = new MobTerrain(level);
        var position = new VectorD(x + 0.5f, y, z + 0.5f);
        if (!terrain.IsRainingAt(new Vector(x, y, z)) || !terrain.IsFree(new EntityDimension { Width = 1.4f, Height = 1.6f }.CreateBBFromPosition(position))) return;
        var horse = new SkeletonHorse { Level = level, EntityId = Server.GetNextEntityId(), Position = position, SkeletonTrap = true };
        horse.InitializeAi();
        if (level.LevelData.Difficulty != Difficulty.Peaceful && random.NextSingle() < Mob.CalculateDifficulty(level.LevelData.Difficulty,
            level.LevelData.Time, chunk is Chunk concrete ? concrete.InhabitedTime : 0, 1) * 0.01f)
            level.SpawnEntity(horse);
    }

    private void SpawnVillageCats(Mob[] mobs)
    {
        foreach (var villager in mobs.OfType<Villager>().Where(villager => !villager.IsBaby).OrderBy(_ => random.Next()).Take(1))
        {
            if (mobs.OfType<Cat>().Count(cat => (cat.Position - villager.Position).MagnitudeSquared() < 2304) >= 5) continue;
            var terrain = new MobTerrain(level);
            var origin = (Vector)villager.Position.Floor();
            var beds = 0;
            for (var x = -8; x <= 8; x++)
            for (var y = -4; y <= 4; y++)
            for (var z = -8; z <= 8; z++)
                if (terrain.GetBlock(new Vector(origin.X + x, origin.Y + y, origin.Z + z)) is { } block &&
                    TagsRegistry.Block.Beds.Entries.Contains(block.RegistryId) && block.GetProperty("part") == "head") beds++;
            if (beds < 5) continue;
            var point = RandomPosition.Find(villager, 8);
            if (point is VectorD position)
                level.SpawnEntity(new Cat { Level = level, EntityId = Server.GetNextEntityId(), Position = position });
        }
    }
}

public abstract partial class AbstractLevel
{
    internal void ScheduleFrogspawn(Vector position, int delay)
    {
        if (GetLoadedChunk(position.X >> 4, position.Z >> 4) is Chunk chunk)
            chunk.FrogspawnTicks[position] = delay;
    }
}
