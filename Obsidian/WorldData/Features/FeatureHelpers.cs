using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Nbt;

namespace Obsidian.WorldData.Features;

/// <summary>
/// Vanilla helpers shared by the configured features: <c>Feature</c> statics, <c>Direction</c> utilities, Java collection
/// ordering and Java/<c>Mth</c> numerics that features depend on for output parity.
/// </summary>
internal static class FeatureHelpers
{
    private static readonly BlockSet dirt = new("#minecraft:dirt");
    private static readonly BlockSet baseStoneOverworld = new("#minecraft:base_stone_overworld");

    // Block entity types of vanilla's RandomizableContainer implementations.
    private static readonly HashSet<string> randomizableContainers =
    [
        "minecraft:chest", "minecraft:trapped_chest", "minecraft:barrel", "minecraft:dispenser", "minecraft:dropper", "minecraft:hopper",
        "minecraft:shulker_box", "minecraft:crafter", "minecraft:decorated_pot"
    ];

    /// <summary>
    /// Vanilla <c>RandomizableContainer.setBlockEntityLootTable</c>: gives the container at <paramref name="position"/> a loot
    /// table and a seed for it. The seed is only drawn when there's such a container.
    /// </summary>
    /// <param name="lootTable">The loot table id, e.g. <c>minecraft:chests/simple_dungeon</c>.</param>
    public static void SetLootTable(IWorldGenLevel level, IRandomSource random, Vector position, string lootTable)
    {
        var container = level.GetBlockEntity(position) as DataBlockEntity;
        if (container is null || !randomizableContainers.Contains(container.Id))
            return;

        container.Set("LootTable", lootTable);

        // Like vanilla, a zero seed isn't saved.
        var seed = random.NextLong();
        if (seed != 0L)
            container.Set("LootTableSeed", seed);
        else
            container.Data.Remove("LootTableSeed");
    }

    /// <summary>
    /// Vanilla <c>StructurePiece.reorient</c>: faces the chest away from its single solid neighbor, or rotates it off walls.
    /// </summary>
    public static IBlock Reorient(IWorldGenLevel level, Vector position, IBlock chest)
    {
        BlockFace? wall = null;
        foreach (var face in Horizontal)
        {
            var neighbor = level.GetBlock(position.Offset(face));
            if (neighbor.Material == Material.Chest)
                return chest;

            if (IsSolidRender(neighbor))
            {
                if (wall is not null)
                {
                    wall = null;
                    break;
                }

                wall = face;
            }
        }

        if (wall is not null)
            return chest.WithProperty("facing", FaceName(wall.Value.Opposite()));

        var facing = ParseFace(chest.GetProperty("facing"));
        if (IsSolidRender(level.GetBlock(position.Offset(facing))))
            facing = facing.Opposite();

        if (IsSolidRender(level.GetBlock(position.Offset(facing))))
            facing = facing.ClockWise();

        if (IsSolidRender(level.GetBlock(position.Offset(facing))))
            facing = facing.Opposite();

        return chest.WithProperty("facing", FaceName(facing));
    }

    /// <summary>
    /// Vanilla <c>BrushableBlockEntity.setLootTable</c>: gives the suspicious sand or gravel at <paramref name="position"/>
    /// the loot table its item comes from.
    /// </summary>
    public static void SetBrushableLootTable(IWorldGenLevel level, Vector position, string lootTable, long seed)
    {
        var brushable = level.GetBlockEntity(position) as DataBlockEntity;
        if (brushable?.Id != "minecraft:brushable_block")
            return;

        brushable.Set("LootTable", lootTable);
        if (seed != 0L)
            brushable.Set("LootTableSeed", seed);
        else
            brushable.Data.Remove("LootTableSeed");
    }

    /// <summary>
    /// Vanilla <c>BlockPos.asLong</c>: the position packed into a long (26 bits X, 26 bits Z, 12 bits Y).
    /// </summary>
    public static long AsLong(Vector position) =>
        (position.X & 0x3FFFFFFL) << 38 | (position.Z & 0x3FFFFFFL) << 12 | (position.Y & 0xFFFL);

    /// <summary>
    /// Vanilla <c>SpawnerBlockEntity.setEntityId</c>: sets the mob the spawner at <paramref name="position"/> spawns.
    /// </summary>
    /// <param name="pickEntity">Picks the entity type id (e.g. <c>minecraft:zombie</c>); like vanilla, only called (and its
    /// random drawn) when there's a spawner.</param>
    public static void SetSpawnerEntity(IWorldGenLevel level, Vector position, Func<string> pickEntity)
    {
        var spawner = level.GetBlockEntity(position) as DataBlockEntity;
        if (spawner?.Id != "minecraft:mob_spawner")
            return;

        spawner.Set(new NbtCompound("SpawnData") { new NbtCompound("entity") { new NbtTag<string>("id", pickEntity()) } });
    }

    /// <summary>
    /// <c>Direction.values()</c> order.
    /// </summary>
    public static readonly BlockFace[] Directions = [BlockFace.Down, BlockFace.Up, BlockFace.North, BlockFace.South, BlockFace.West, BlockFace.East];

    /// <summary>
    /// <c>Direction.Plane.HORIZONTAL</c> order (north, east, south, west), which differs from <see cref="BlockFace"/> order.
    /// </summary>
    public static readonly BlockFace[] Horizontal = [BlockFace.North, BlockFace.East, BlockFace.South, BlockFace.West];

    /// <summary>
    /// The position <paramref name="distance"/> blocks toward <paramref name="face"/> (vanilla <c>BlockPos.relative</c>).
    /// </summary>
    public static Vector Offset(this Vector position, BlockFace face, int distance = 1) => position + face.ToVector() * distance;

    public static Vector AtY(this Vector position, int y) => new(position.X, y, position.Z);

    public static BlockFace ClockWise(this BlockFace face) => face switch
    {
        BlockFace.North => BlockFace.East,
        BlockFace.East => BlockFace.South,
        BlockFace.South => BlockFace.West,
        BlockFace.West => BlockFace.North,
        _ => throw new ArgumentOutOfRangeException(nameof(face))
    };

    public static BlockFace CounterClockWise(this BlockFace face) => face switch
    {
        BlockFace.North => BlockFace.West,
        BlockFace.West => BlockFace.South,
        BlockFace.South => BlockFace.East,
        BlockFace.East => BlockFace.North,
        _ => throw new ArgumentOutOfRangeException(nameof(face))
    };

    public static bool IsVertical(this BlockFace face) => face is BlockFace.Up or BlockFace.Down;

    public static bool SameAxis(BlockFace a, BlockFace b) => a == b || a == b.Opposite();

    /// <summary>
    /// Block state property name for a face (<c>north</c>, <c>up</c>...), as used by vines and multiface blocks.
    /// </summary>
    public static string FaceName(BlockFace face) => face switch
    {
        BlockFace.Down => "down",
        BlockFace.Up => "up",
        BlockFace.North => "north",
        BlockFace.South => "south",
        BlockFace.West => "west",
        BlockFace.East => "east",
        _ => throw new ArgumentOutOfRangeException(nameof(face))
    };

    public static BlockFace ParseFace(string? name) => name switch
    {
        "down" => BlockFace.Down,
        "up" => BlockFace.Up,
        "north" => BlockFace.North,
        "south" => BlockFace.South,
        "west" => BlockFace.West,
        "east" => BlockFace.East,
        _ => throw new ArgumentException($"Unknown direction '{name}'.", nameof(name))
    };

    /// <summary>
    /// Vanilla <c>Direction.getRandom</c>.
    /// </summary>
    public static BlockFace RandomDirection(IRandomSource random) => Directions[random.NextInt(Directions.Length)];

    /// <summary>
    /// Vanilla <c>Direction.Plane.HORIZONTAL.getRandomDirection</c>.
    /// </summary>
    public static BlockFace RandomHorizontal(IRandomSource random) => Horizontal[random.NextInt(Horizontal.Length)];

    /// <summary>
    /// Vanilla <c>Util.shuffle</c>: Fisher-Yates from the end, one <c>nextInt(i)</c> per step.
    /// </summary>
    public static void Shuffle<T>(IList<T> list, IRandomSource random)
    {
        for (var i = list.Count; i > 1; i--)
        {
            var j = random.NextInt(i);
            (list[i - 1], list[j]) = (list[j], list[i - 1]);
        }
    }

    /// <summary>
    /// Vanilla <c>Util.shuffledCopy</c>.
    /// </summary>
    public static List<T> ShuffledCopy<T>(IEnumerable<T> source, IRandomSource random)
    {
        var list = new List<T>(source);
        Shuffle(list, random);
        return list;
    }

    /// <summary>
    /// Orders positions the way iterating a <c>java.util.HashSet&lt;BlockPos&gt;</c> filled in this order would.
    /// </summary>
    /// <remarks>
    /// Several features consume randomness while iterating such sets, so the order is part of the output. Java buckets
    /// entries by <c>(h ^ h &gt;&gt;&gt; 16) &amp; (capacity - 1)</c> with <c>h = (y + z * 31) * 31 + x</c>, keeps insertion
    /// order inside a bucket (resizes preserve it) and doubles the table from 16 while size exceeds 75% of capacity.
    /// Duplicates keep their first position, like <c>HashSet.add</c>.
    /// </remarks>
    public static List<Vector> JavaHashSetOrder(IEnumerable<Vector> insertionOrder)
    {
        var seen = new HashSet<Vector>();
        var distinct = new List<Vector>();
        foreach (var position in insertionOrder)
        {
            if (seen.Add(position))
                distinct.Add(position);
        }

        var capacity = 16;
        while (distinct.Count > capacity * 3 / 4)
            capacity <<= 1;

        // OrderBy is stable, so same-bucket entries keep insertion order.
        return [.. distinct.OrderBy(position => Bucket(position, capacity))];
    }

    private static int Bucket(Vector position, int capacity)
    {
        var hash = unchecked((position.Y + position.Z * 31) * 31 + position.X);
        return (hash ^ (int)((uint)hash >> 16)) & (capacity - 1);
    }

    /// <summary>
    /// Vanilla <c>BlockPos.betweenClosed</c>: every position in the box, x fastest, then y, then z.
    /// </summary>
    public static IEnumerable<Vector> BetweenClosed(Vector min, Vector max)
    {
        for (var z = min.Z; z <= max.Z; z++)
        {
            for (var y = min.Y; y <= max.Y; y++)
            {
                for (var x = min.X; x <= max.X; x++)
                    yield return new Vector(x, y, z);
            }
        }
    }

    /// <summary>
    /// Vanilla <c>BlockPos.randomBetweenClosed</c>: <paramref name="count"/> random positions in the box, each drawing x, y, z
    /// with <c>nextInt(size)</c> in that order (lazily, so draws interleave with the caller's use of each position).
    /// </summary>
    public static IEnumerable<Vector> RandomBetweenClosed(IRandomSource random, int count, Vector min, Vector max)
    {
        var sizeX = max.X - min.X + 1;
        var sizeY = max.Y - min.Y + 1;
        var sizeZ = max.Z - min.Z + 1;

        for (var i = 0; i < count; i++)
        {
            var x = min.X + random.NextInt(sizeX);
            var y = min.Y + random.NextInt(sizeY);
            var z = min.Z + random.NextInt(sizeZ);
            yield return new Vector(x, y, z);
        }
    }

    /// <summary>
    /// Vanilla <c>BlockPos.withinManhattan</c>: positions within the given per-axis reach, ordered by Manhattan distance
    /// (x, then y ascending inside each depth; each nonzero z offset is followed by its mirror).
    /// </summary>
    public static IEnumerable<Vector> WithinManhattan(Vector center, int reachX, int reachY, int reachZ)
    {
        var maxDepth = reachX + reachY + reachZ;
        var depth = 0;
        var maxX = 0;
        var maxY = 0;
        var x = 0;
        var y = 0;

        while (true)
        {
            int foundX, foundY, foundZ;
            while (true)
            {
                if (y > maxY)
                {
                    x++;
                    if (x > maxX)
                    {
                        depth++;
                        if (depth > maxDepth)
                            yield break;

                        maxX = Math.Min(reachX, depth);
                        x = -maxX;
                    }

                    maxY = Math.Min(reachY, depth - Math.Abs(x));
                    y = -maxY;
                }

                var z = depth - Math.Abs(x) - Math.Abs(y);
                var candidateY = y;
                y++;

                if (z <= reachZ)
                {
                    foundX = x;
                    foundY = candidateY;
                    foundZ = z;
                    break;
                }
            }

            yield return center + new Vector(foundX, foundY, foundZ);

            if (foundZ != 0)
                yield return center + new Vector(foundX, foundY, -foundZ);
        }
    }

    /// <summary>
    /// Vanilla <c>Mth.nextInt(random, min, max)</c>: <paramref name="min"/> without drawing when the range is empty.
    /// </summary>
    public static int NextInt(IRandomSource random, int min, int max) => min >= max ? min : random.NextInt(max - min + 1) + min;

    /// <summary>
    /// Vanilla <c>Vec3i.distSqr</c> (computed in doubles).
    /// </summary>
    public static double DistSqr(Vector a, Vector b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        double dz = a.Z - b.Z;
        return dx * dx + dy * dy + dz * dz;
    }

    public static int DistManhattan(Vector a, Vector b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) + Math.Abs(a.Z - b.Z);

    // ---- Feature.java statics ----

    /// <summary>
    /// Vanilla <c>Feature.isStone</c> (<c>#minecraft:base_stone_overworld</c>).
    /// </summary>
    public static bool IsStone(IBlock block) => baseStoneOverworld.Contains(block);

    /// <summary>
    /// Vanilla <c>Feature.isDirt</c> (<c>#minecraft:dirt</c>).
    /// </summary>
    public static bool IsDirt(IBlock block) => dirt.Contains(block);

    /// <summary>
    /// Vanilla <c>Feature.isAdjacentToAir</c>: any of the 6 neighbors is air.
    /// </summary>
    public static bool IsAdjacentToAir(IWorldGenLevel level, Vector position)
    {
        foreach (var face in Directions)
        {
            if (level.GetBlock(position.Offset(face)).IsAir)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Vanilla <c>Feature.safeSetBlock</c>: sets the block only if the current block passes <paramref name="canReplace"/>.
    /// </summary>
    public static void SafeSetBlock(IWorldGenLevel level, Vector position, IBlock block, Func<IBlock, bool> canReplace)
    {
        if (canReplace(level.GetBlock(position)))
            level.SetBlock(position, block);
    }

    /// <summary>
    /// Vanilla <c>FluidTags.WATER</c> test (source or flowing water, including waterlogged blocks).
    /// </summary>
    public static bool IsWaterFluid(IBlock block) => block.GetFluid() is FluidKind.Water or FluidKind.FlowingWater;

    public static bool IsLavaFluid(IBlock block) => block.GetFluid() is FluidKind.Lava or FluidKind.FlowingLava;

    /// <summary>
    /// Vanilla <c>BlockState.isSolidRender()</c>, approximated from collision and solidity data.
    /// </summary>
    /// <remarks>
    /// Vanilla needs an opaque full-cube occlusion shape. Obsidian has no occlusion data, so this treats solid full-collision
    /// cubes as solid render except the see-through classes (leaves, glass, ice, spawners and the like).
    /// </remarks>
    public static bool IsSolidRender(IBlock block)
    {
        if (!block.IsSolid() || !block.IsCollisionShapeFullBlock())
            return false;

        return block.BlockClass() switch
        {
            "TintedParticleLeavesBlock" or "UntintedParticleLeavesBlock" or "MangroveLeavesBlock" or "TransparentBlock"
                or "StainedGlassBlock" or "TintedGlassBlock" or "HalfTransparentBlock" or "IceBlock" or "FrostedIceBlock"
                or "SpawnerBlock" or "TrialSpawnerBlock" or "VaultBlock" or "BeaconBlock" or "SlimeBlock" or "HoneyBlock"
                or "WaterloggedTransparentBlock" or "WeatheringCopperGrateBlock" or "PowderSnowBlock" => false,
            _ => true
        };
    }

    // ---- Java / Mth numerics ----

    /// <summary>
    /// Java <c>Math.round(float)</c>: <c>floor(x + 0.5)</c> evaluated exactly.
    /// </summary>
    public static int JavaRound(float value) => (int)Math.Floor((double)value + 0.5);

    /// <summary>
    /// Vanilla <c>Mth.ceil(float)</c>.
    /// </summary>
    public static int Ceil(float value)
    {
        var truncated = (int)value;
        return value > truncated ? truncated + 1 : truncated;
    }

    /// <summary>
    /// Vanilla <c>Mth.ceil(double)</c>.
    /// </summary>
    public static int Ceil(double value)
    {
        var truncated = (int)value;
        return value > truncated ? truncated + 1 : truncated;
    }

    public static double ClampedLerp(double delta, double start, double end) =>
        delta < 0.0 ? start : delta > 1.0 ? end : start + delta * (end - start);

    public static float ClampedLerp(float delta, float start, float end) =>
        delta < 0.0f ? start : delta > 1.0f ? end : start + delta * (end - start);

    /// <summary>
    /// Vanilla <c>Mth.clampedMap(double...)</c>.
    /// </summary>
    public static double ClampedMap(double value, double fromMin, double fromMax, double toMin, double toMax) =>
        ClampedLerp((value - fromMin) / (fromMax - fromMin), toMin, toMax);

    /// <summary>
    /// Vanilla <c>Mth.clampedMap(float...)</c>.
    /// </summary>
    public static float ClampedMap(float value, float fromMin, float fromMax, float toMin, float toMax) =>
        ClampedLerp((value - fromMin) / (fromMax - fromMin), toMin, toMax);

    /// <summary>
    /// Vanilla <c>Mth.randomBetweenInclusive</c>.
    /// </summary>
    public static int RandomBetweenInclusive(IRandomSource random, int min, int max) => random.NextInt(max - min + 1) + min;

    /// <summary>
    /// Vanilla <c>Mth.randomBetween(float)</c>.
    /// </summary>
    public static float RandomBetween(IRandomSource random, float min, float max) => random.NextFloat() * (max - min) + min;

    /// <summary>
    /// Vanilla <c>ClampedNormalFloat.sample(random, mean, deviation, min, max)</c>.
    /// </summary>
    public static float ClampedNormal(IRandomSource random, float mean, float deviation, float min, float max) =>
        Math.Clamp(mean + (float)random.NextGaussian() * deviation, min, max);
}

/// <summary>
/// Vanilla <c>Column</c>: the open space found by scanning up and down from a position, with optional floor and ceiling Ys.
/// </summary>
internal readonly record struct Column(int? Floor, int? Ceiling)
{
    /// <summary>
    /// Space between floor and ceiling (exclusive), or <c>null</c> unless both exist.
    /// </summary>
    public int? Height => this.Floor is int floor && this.Ceiling is int ceiling ? ceiling - floor - 1 : null;

    public bool IsRange => this.Floor.HasValue && this.Ceiling.HasValue;

    /// <summary>
    /// Vanilla <c>Column.scan</c>: <c>null</c> if the start isn't <paramref name="inside"/>; otherwise walks up to
    /// <paramref name="searchRange"/> blocks each way and records the first <paramref name="edge"/> block reached.
    /// </summary>
    public static Column? Scan(IWorldGenLevel level, Vector position, int searchRange, Func<IBlock, bool> inside, Func<IBlock, bool> edge)
    {
        if (!inside(level.GetBlock(position)))
            return null;

        var ceiling = ScanDirection(level, position, searchRange, inside, edge, 1);
        var floor = ScanDirection(level, position, searchRange, inside, edge, -1);
        return new Column(floor, ceiling);
    }

    private static int? ScanDirection(IWorldGenLevel level, Vector start, int searchRange, Func<IBlock, bool> inside, Func<IBlock, bool> edge,
        int step)
    {
        var position = start;
        for (var i = 1; i < searchRange && inside(level.GetBlock(position)); i++)
            position += new Vector(0, step, 0);

        return edge(level.GetBlock(position)) ? position.Y : null;
    }
}
