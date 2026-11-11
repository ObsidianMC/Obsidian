using Obsidian.ChunkData;
using Obsidian.Entities;
using Obsidian.Entities.AI;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    // Vanilla's 1 in 100,000 chance a chunk is struck each tick of a thunderstorm.
    private const int ThunderChance = 100000;

    // How far vanilla looks for a lightning rod to take a strike, and for living things under the sky around it.
    private const int RodReach = 128;
    private const int TargetReach = 3;

    /// <summary>
    /// Whether natural strikes can happen this tick: it rains and thunders where the level has weather. Read once a
    /// tick, so that each chunk's own check is a single draw.
    /// </summary>
    internal bool StormStrikes => this.IsRaining() && this.IsThundering();

    /// <summary>
    /// Vanilla's <c>ServerLevel.tickThunder</c> for a chunk that spawns mobs, while <see cref="StormStrikes"/> holds:
    /// by a 1 in 100,000 chance a bolt strikes the chunk's next random column, or the nearest lightning rod on top of
    /// its column within 128 blocks, or a living thing under the sky near the column. With mob spawning on, it may be
    /// a skeleton trap, whose bolt only shows.
    /// </summary>
    /// <remarks>
    /// The draws come from the level random in vanilla's order (the chance, the living thing struck, the trap's
    /// chance), and the column from the random tick position sequence, as vanilla's do.
    /// </remarks>
    internal void TickThunder(IChunk chunk)
    {
        var random = this.GrowthRandom;
        if (random.NextInt(ThunderChance) != 0)
            return;

        var (x, _, z) = this.NextRandomTickOffset();
        var column = new Vector(chunk.X * 16 + x, 0, chunk.Z * 16 + z);
        var target = this.FindLightningTarget(column);
        if (!this.RainsOn(target))
            return;

        var targetChunk = this.GetLoadedChunk(target.X >> 4, target.Z >> 4);
        var trap = this.LevelData.GetBooleanRule("spawn_mobs")
            && random.NextDouble() < this.StrikeDifficulty(targetChunk) * 0.01
            && !this.IsLightningRod(target + Vector.Down);
        if (trap)
            this.SpawnSkeletonTrap(target);

        var bolt = new LightningBolt
        {
            Level = this,
            EntityId = Server.GetNextEntityId(),
            Position = new VectorD(target.X + 0.5, target.Y, target.Z + 0.5),
            VisualOnly = trap
        };
        this.SpawnEntity(bolt);
    }

    // Vanilla's findLightningTargetAround: the top of the column (by the motion blocking heightmap), unless a lightning
    // rod or a living thing under the sky takes the strike.
    private Vector FindLightningTarget(Vector column)
    {
        var center = column with { Y = this.MotionBlockingHeight(column) };
        if (this.FindLightningRod(center) is Vector rod)
            return rod + Vector.Up;

        var struck = this.LivingUnderTheSky(center);
        if (struck.Count > 0)
            return (Vector)struck[this.GrowthRandom.NextInt(struck.Count)].Position.Floor();

        return center.Y == this.MinY - 1 ? center + new Vector(0, 2, 0) : center;
    }

    // The closest lightning rod within 128 blocks of the center that is its column's top block (by the world surface
    // heightmap), as vanilla's lightning rod point of interest search finds it. Only sections whose palettes hold a rod
    // are read, and this runs only when a strike comes up.
    private Vector? FindLightningRod(Vector center)
    {
        const int chunkReach = RodReach / 16 + 1;
        Vector? closest = null;
        var closestDistance = long.MaxValue;
        var (centerChunkX, centerChunkZ) = (center.X >> 4, center.Z >> 4);
        for (var chunkX = centerChunkX - chunkReach; chunkX <= centerChunkX + chunkReach; chunkX++)
        {
            for (var chunkZ = centerChunkZ - chunkReach; chunkZ <= centerChunkZ + chunkReach; chunkZ++)
            {
                if (this.GetLoadedChunk(chunkX, chunkZ) is not Chunk chunk)
                    continue;

                foreach (var rod in LightningRodsIn(chunk))
                {
                    var distance = DistanceSquared(rod, center);
                    if (distance > (long)RodReach * RodReach || distance >= closestDistance)
                        continue;

                    if (rod.Y != chunk.GetHeight(HeightmapType.WorldSurface, rod.X, rod.Z) - 1)
                        continue;

                    closest = rod;
                    closestDistance = distance;
                }
            }
        }

        return closest;
    }

    private static List<Vector> LightningRodsIn(Chunk chunk)
    {
        var rods = new List<Vector>();
        var sections = chunk.Sections;
        for (var index = 0; index < sections.Length; index++)
        {
            if (sections[index] is not ChunkSection section || !section.MayHold(IsLightningRodState))
                continue;

            for (var y = 0; y < 16; y++)
            {
                for (var z = 0; z < 16; z++)
                {
                    for (var x = 0; x < 16; x++)
                    {
                        if (IsLightningRodState(section.GetStateId(x, y, z)))
                            rods.Add(new Vector(chunk.X * 16 + x, chunk.MinY + index * 16 + y, chunk.Z * 16 + z));
                    }
                }
            }
        }

        return rods;
    }

    private static bool IsLightningRodState(int stateId) => LightningRods.IsRod(BlocksRegistry.Get(stateId));

    private bool IsLightningRod(Vector position) =>
        this.GameEventBlock(position) is IBlock block && LightningRods.IsRod(block);

    private static long DistanceSquared(Vector from, Vector to)
    {
        long dx = from.X - to.X;
        long dy = from.Y - to.Y;
        long dz = from.Z - to.Z;
        return dx * dx + dy * dy + dz * dz;
    }

    // The living things in the box from three blocks around the center up to three above the top of the world, that
    // stand under the open sky, in the order they were added.
    private List<IEntity> LivingUnderTheSky(Vector center)
    {
        var min = new VectorD(center.X - TargetReach, center.Y - TargetReach, center.Z - TargetReach);
        var max = new VectorD(
            center.X + 1 + TargetReach,
            this.MinY + this.Height + 1 + TargetReach,
            center.Z + 1 + TargetReach);
        var terrain = new MobTerrain(this);
        var found = new List<IEntity>();
        foreach (var entity in this.EntitiesInColumn(min, max))
        {
            if (entity is not Living living || living.Health <= 0 || entity is Mob { IsRemoved: true })
                continue;

            if (terrain.GetSkyLight((Vector)entity.Position.Floor()) == 15)
                found.Add(entity);
        }

        found.Sort(static (left, right) => left.EntityId.CompareTo(right.EntityId));
        return found;
    }

    // The entities whose boxes overlap a tall box: those near its column, at any height.
    private IEnumerable<IEntity> EntitiesInColumn(VectorD min, VectorD max)
    {
        var middle = new VectorD((min.X + max.X) / 2, 0, (min.Z + max.Z) / 2);
        var across = Math.Max(max.X - min.X, max.Z - min.Z);
        var deepest = Math.Max(Math.Abs(this.MinY), Math.Abs(this.MinY + this.Height));
        foreach (var entity in this.GetEntitiesInRange(middle, (float)(deepest + across + 4)))
        {
            var box = entity is Entity concrete ? concrete.CurrentBox : entity.BoundingBox;
            if (box.Min.X < max.X && box.Max.X > min.X
                && box.Min.Y < max.Y && box.Max.Y > min.Y
                && box.Min.Z < max.Z && box.Max.Z > min.Z)
            {
                yield return entity;
            }
        }
    }

    private int MotionBlockingHeight(Vector column) =>
        this.GetLoadedChunk(column.X >> 4, column.Z >> 4) is IChunk chunk
            ? chunk.GetHeight(HeightmapType.MotionBlocking, column.X, column.Z)
            : this.MinY;

    // Vanilla's Level.isRainingAt: the sky is open above the position, nothing below the top of the column is above
    // it, and its biome has rain there.
    private bool RainsOn(Vector position)
    {
        if (this.IsOutsideBuildHeight(position.Y))
            return false;

        if (this.GetLoadedChunk(position.X >> 4, position.Z >> 4) is not IChunk chunk)
            return false;

        if (new MobTerrain(this).GetSkyLight(position) < 15)
            return false;

        if (this.MotionBlockingHeight(position) > position.Y)
            return false;

        var biome = this.BiomeAt(chunk, position.X, position.Y, position.Z);
        return this.PrecipitationAt(biome, position) == Precipitation.Rain;
    }

    // Vanilla's getCurrentDifficultyAt, which the skeleton trap's chance scales with.
    private float StrikeDifficulty(IChunk? chunk)
    {
        var inhabited = chunk is Chunk concrete ? concrete.InhabitedTime : 0;
        ReadOnlySpan<float> moon = [1, 0.75f, 0.5f, 0.25f, 0, 0.25f, 0.5f, 0.75f];
        var phase = (int)((this.LevelData.Time / 24000 % 8 + 8) % 8);
        return Mob.CalculateDifficulty(this.LevelData.Difficulty, this.LevelData.GameTime, inhabited, moon[phase]);
    }

    // A skeleton horse at the strike's block corner, as vanilla places it, that rides out a skeleton trap.
    private void SpawnSkeletonTrap(Vector position)
    {
        var horse = new SkeletonHorse
        {
            Level = this,
            EntityId = Server.GetNextEntityId(),
            Position = (VectorD)position,
            SkeletonTrap = true
        };
        this.SpawnEntity(horse);
    }
}
