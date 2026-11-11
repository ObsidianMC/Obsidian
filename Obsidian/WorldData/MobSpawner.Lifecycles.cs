using Obsidian.Entities;
using Obsidian.Entities.AI;
using Obsidian.Nbt;

namespace Obsidian.WorldData;

internal sealed partial class MobSpawner
{
    private sealed record TimedHiveBee(Bee Bee, NbtCompound Occupant, IBlock HiveState);

    private void TickHive(DataBlockEntity hive)
    {
        List<TimedHiveBee> released;
        lock (level.Fluids.TickLock)
            released = DetachTimedHiveBees(hive);

        SpawnTimedHiveBees(hive, released);
    }

    private void SpawnTimedHiveBees(DataBlockEntity hive, List<TimedHiveBee> released)
    {
        IBlock? expected = null;
        foreach (var release in released)
        {
            expected ??= release.HiveState;
            var bee = release.Bee;
            var deliveredNectar = bee.HasNectar;
            bee.HasNectar = false;

            // Admission can reject one UUID or an unloaded region. Each detached occupant is independent:
            // restore just the rejected one, without overwriting occupants changed by a concurrent harvest/entry.
            if (!level.TrySpawnEntity(bee, hostOwned: true))
            {
                lock (level.Fluids.TickLock)
                {
                    if (ReferenceEquals(level.GetResidentBlockEntity(hive.BlockPosition), hive))
                    {
                        var occupants = hive.SnapshotTag<NbtList>("bees")
                            ?? new NbtList(NbtTagType.Compound, "bees");
                        occupants.Add(release.Occupant);
                        hive.Set(occupants);
                    }
                }

                continue;
            }

            // Failed admission must not deliver nectar. Keep the state from detachment so a harvest that
            // completed while spawning was busy still invalidates the queued delivery.
            if (deliveredNectar)
            {
                var honey = int.TryParse(expected.GetProperty("honey_level"), out var value) ? value : 0;
                var updated = expected.WithProperty("honey_level",
                    Math.Min(5, honey + (random.Next(100) == 0 ? 2 : 1)));
                QueueHiveHoney(hive, expected, updated);
                expected = updated;
            }
        }
    }

    // The same tick lock protects harvest, entry and timed release; no inventory or entity spawn is waited for here.
    private List<TimedHiveBee> DetachTimedHiveBees(DataBlockEntity hive)
    {
        if (!ReferenceEquals(level.GetResidentBlockEntity(hive.BlockPosition), hive)
            || hive.SnapshotTag<NbtList>("bees") is not NbtList occupants)
        {
            return [];
        }

        var terrain = new MobTerrain(level);
        var block = terrain.GetBlock(hive.BlockPosition);
        if (block?.RegistryId is not BlockIds.BeeNest and not BlockIds.Beehive)
            return [];

        var facing = Features.FeatureHelpers.ParseFace(block.GetProperty("facing")).ToVector();
        var position = (VectorD)(hive.BlockPosition + facing) + new VectorD(0.5f, 0.2f, 0.5f);
        var retained = new NbtList(NbtTagType.Compound, "bees");
        var released = new List<TimedHiveBee>();
        foreach (var occupant in occupants.OfType<NbtCompound>())
        {
            var elapsed = occupant.GetInt("ticks_in_hive") + 1;
            occupant.Set(new NbtTag<int>("ticks_in_hive", elapsed));
            var tooEarly = elapsed < Math.Max(600, occupant.GetInt("min_ticks_in_hive"));
            if (tooEarly || level.LevelData.Raining || level.DayTime is >= 12000 and < 23000
                || !occupant.TryGetTag<NbtCompound>("entity_data", out var saved))
            {
                retained.Add(occupant);
                continue;
            }

            saved.Set(EntityNbt.DoubleList("Pos", position));
            if (EntityNbt.Load(saved, level) is not Bee bee
                || !terrain.IsFree(bee.Dimension.CreateBBFromPosition(position)))
            {
                retained.Add(occupant);
                continue;
            }

            bee.HivePosition = hive.BlockPosition;
            released.Add(new TimedHiveBee(bee, occupant, block));
        }

        hive.Set(retained);
        return released;
    }

    private void QueueHiveHoney(DataBlockEntity hive, IBlock expected, IBlock updated)
    {
        level.EnqueueEntityAction(() =>
        {
            lock (level.Fluids.TickLock)
            {
                // A completed harvest or replacement owns the new state. A delivery computed earlier must
                // not overwrite it, even if the delayed action still has the original block entity.
                var current = level.BlockUpdates.GetBlock(hive.BlockPosition);
                if (ReferenceEquals(level.GetResidentBlockEntity(hive.BlockPosition), hive)
                    && current.IsSameState(expected))
                {
                    level.BlockUpdates.SetBlock(hive.BlockPosition, updated, API.Blocks.BlockUpdateFlags.All);
                }
            }

            return ValueTask.CompletedTask;
        });
    }

    private void TickThunder(IChunk chunk)
    {
        if (level.DimensionName != "minecraft:overworld" || !level.LevelData.Thundering || !level.LevelData.Raining || random.Next(100000) != 0)
            return;
        var x = chunk.X * 16 + random.Next(16);
        var z = chunk.Z * 16 + random.Next(16);
        var y = GetSpawnHeight(chunk, x, z);
        var terrain = new MobTerrain(level);
        var position = new VectorD(x + 0.5f, y, z + 0.5f);
        if (!terrain.IsRainingAt(new Vector(x, y, z)) || !terrain.IsFree(new EntityDimension { Width = 1.4f, Height = 1.6f }.CreateBBFromPosition(position)))
            return;
        var horse = new SkeletonHorse { Level = level, EntityId = Server.GetNextEntityId(), Position = position, SkeletonTrap = true };
        horse.InitializeAi();
        var inhabited = chunk is Chunk concrete ? concrete.InhabitedTime : 0;
        var difficulty = Mob.CalculateDifficulty(level.LevelData.Difficulty, level.LevelData.GameTime, inhabited, 1);
        if (level.LevelData.Difficulty != Difficulty.Peaceful && random.NextSingle() < difficulty * 0.01f)
            level.SpawnEntity(horse);
    }

    private void SpawnVillageCats(List<Mob> mobs)
    {
        foreach (var villager in mobs.OfType<Villager>().Where(villager => !villager.IsBaby).OrderBy(_ => random.Next()).Take(1))
        {
            if (mobs.OfType<Cat>().Count(cat => (cat.Position - villager.Position).MagnitudeSquared() < 2304) >= 5)
                continue;
            var terrain = new MobTerrain(level);
            var origin = (Vector)villager.Position.Floor();
            var beds = 0;
            for (var x = -8; x <= 8; x++)
            {
                for (var y = -4; y <= 4; y++)
                {
                    for (var z = -8; z <= 8; z++)
                    {
                        if (terrain.GetBlock(new Vector(origin.X + x, origin.Y + y, origin.Z + z)) is { } block &&
                                    TagsRegistry.Block.Beds.Entries.Contains(block.RegistryId) && block.GetProperty("part") == "head")
                        {
                            beds++;
                        }
                    }
                }
            }
            if (beds < 5)
                continue;
            var point = RandomPosition.Find(villager, 8);
            if (point is VectorD position)
                level.SpawnEntity(new Cat { Level = level, EntityId = Server.GetNextEntityId(), Position = position });
        }
    }
}
