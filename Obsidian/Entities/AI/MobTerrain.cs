using Obsidian.WorldData;
using System.Threading;
using Obsidian.WorldData.Fluids;

namespace Obsidian.Entities.AI;

internal sealed class MobTerrain(ILevel level)
{
    public bool IsInFluid(VectorD point, bool water)
    {
        var position = (Vector)point.Floor();
        if (GetBlock(position) is not { } block) return false;
        var fluid = FluidState.Of(block);
        if (!(water ? fluid.IsWater : fluid.IsLava)) return false;
        var above = GetBlock(position + Vector.Up);
        var aboveFluid = above == null ? FluidState.Empty : FluidState.Of(above);
        var height = (water ? aboveFluid.IsWater : aboveFluid.IsLava) ? 1 : fluid.OwnHeight;
        return point.Y < position.Y + height;
    }

    public bool ContainsFluid(BoundingBox bounds, bool water)
    {
        for (var x = (int)Math.Floor(bounds.Min.X + 0.001); x < (int)Math.Ceiling(bounds.Max.X - 0.001); x++)
        for (var y = (int)Math.Floor(bounds.Min.Y + 0.001); y < (int)Math.Ceiling(bounds.Max.Y - 0.001); y++)
        for (var z = (int)Math.Floor(bounds.Min.Z + 0.001); z < (int)Math.Ceiling(bounds.Max.Z - 0.001); z++)
            if (IsInFluid(new VectorD(x + 0.5, Math.Max(y, bounds.Min.Y + 0.001), z + 0.5), water)) return true;
        return false;
    }

    public VectorD FluidPush(BoundingBox bounds, bool water, VectorD motion)
    {
        if (level is not AbstractLevel concrete) return VectorD.Zero;
        var flow = VectorD.Zero;
        var count = 0;
        var depth = 0d;
        for (var x = (int)Math.Floor(bounds.Min.X + 0.001); x < (int)Math.Ceiling(bounds.Max.X - 0.001); x++)
        for (var y = (int)Math.Floor(bounds.Min.Y + 0.001); y < (int)Math.Ceiling(bounds.Max.Y - 0.001); y++)
        for (var z = (int)Math.Floor(bounds.Min.Z + 0.001); z < (int)Math.Ceiling(bounds.Max.Z - 0.001); z++)
        {
            var point = new Vector(x, y, z);
            if (GetBlock(point) is not { } block) continue;
            var fluid = FluidState.Of(block);
            if (!(water ? fluid.IsWater : fluid.IsLava)) continue;
            var above = GetBlock(point + Vector.Up);
            var full = above != null && (water ? FluidState.Of(above).IsWater : FluidState.Of(above).IsLava);
            var surface = y + (full ? 1 : fluid.OwnHeight);
            if (surface < bounds.Min.Y + 0.001) continue;
            depth = Math.Max(depth, surface - bounds.Min.Y - 0.001);
            var current = concrete.Fluids.GetFlow(point);
            if (depth < 0.4) current *= depth;
            flow += current;
            count++;
        }
        if (count == 0 || flow.Magnitude == 0) return VectorD.Zero;
        flow /= count;
        flow /= flow.Magnitude;
        flow *= water ? 0.014 : concrete.DimensionName == "minecraft:the_nether" ? 0.007 : 0.0023333333333333335;
        if (Math.Abs(motion.X) < 0.003 && Math.Abs(motion.Z) < 0.003 && flow.Magnitude < 0.0045)
            flow *= 0.0045 / flow.Magnitude;
        return flow;
    }

    public int GetSkyLight(Vector position) => level is AbstractLevel concrete && !concrete.IsOutsideBuildHeight(position.Y) ?
        concrete.GetLoadedChunk(position.X >> 4, position.Z >> 4)?.GetLightLevel(position.X, position.Y, position.Z, LightType.Sky) ?? 0 : 0;

    public int GetBlockLight(Vector position) => level is AbstractLevel concrete && !concrete.IsOutsideBuildHeight(position.Y) ?
        concrete.GetLoadedChunk(position.X >> 4, position.Z >> 4)?.GetLightLevel(position.X, position.Y, position.Z, LightType.Block) ?? 0 : 0;

    public float GetTemperature(Vector position)
    {
        if (level is not AbstractLevel concrete || concrete.GetLoadedChunk(position.X >> 4, position.Z >> 4) is not { } chunk)
            return 0.5f;
        var seaLevel = concrete.Generator is Obsidian.WorldData.Generators.MojangGenerator generator
            ? generator.SeaLevel : 63;
        return Obsidian.WorldData.Generators.Mojang.BiomeTemperature.GetTemperature(
            chunk.GetBiome(position.X, position.Y, position.Z), position.X, position.Y, position.Z, seaLevel);
    }

    public bool IsRainingAt(Vector position) => level.LevelData.Raining && GetSkyLight(position) == 15 &&
        level is AbstractLevel concrete && concrete.GetLoadedChunk(position.X >> 4, position.Z >> 4) is { } chunk &&
        chunk.GetBiome(position.X, position.Y, position.Z).Element.HasPrecipitation && GetTemperature(position) >= 0.15f;

    public float GetFriction(VectorD position) =>
        GetBlock((Vector)(position - new VectorD(0, 0.500001f, 0)).Floor())?.RegistryId switch
    {
        BlockIds.Ice or BlockIds.PackedIce or BlockIds.FrostedIce => 0.98f,
        BlockIds.BlueIce => 0.989f,
        BlockIds.SlimeBlock => 0.8f,
        _ => 0.6f
    };
    // The chunk the last block was read from, kept for the rest of the level's tick: a mob reads a few dozen blocks
    // around it a tick, nearly all from one chunk, and each read looked the chunk up in the level's regions. It's
    // replaced whole, as a packet's handler may read the mob's terrain while its tick does.
    private LastChunk? lastChunk;

    private sealed record LastChunk(long Tick, int X, int Z, IChunk? Chunk);

    public IBlock? GetBlock(Vector position)
    {
        if (level is not AbstractLevel concrete || concrete.IsOutsideBuildHeight(position.Y))
            return null;

        var (chunkX, chunkZ) = (position.X >> 4, position.Z >> 4);
        var tick = concrete.CurrentTickId;
        if (tick != 0 && Volatile.Read(ref lastChunk) is LastChunk last && last.Tick == tick && last.X == chunkX && last.Z == chunkZ)
            return last.Chunk?.GetBlock(position);

        var chunk = concrete.GetLoadedChunk(chunkX, chunkZ);
        if (tick != 0)
            Volatile.Write(ref lastChunk, new LastChunk(tick, chunkX, chunkZ, chunk));
        return chunk?.GetBlock(position);
    }

    public IEnumerable<BoundingBox> GetCollisions(BoundingBox bounds, bool ignoreWoodenDoors = false) =>
        GetBlockCollisions(bounds, ignoreWoodenDoors).Select(collision => collision.Shape);

    /// <summary>
    /// The collision shapes near <paramref name="bounds"/>, each with the position of the block it belongs to, which
    /// it may reach out of (a fence above, a pitcher crop below).
    /// </summary>
    public IEnumerable<(BoundingBox Shape, Vector Block)> GetBlockCollisions(
        BoundingBox bounds,
        bool ignoreWoodenDoors = false)
    {
        // Shapes such as fences can extend above their block's unit cube.
        for (var x = (int)Math.Floor(bounds.Min.X); x <= (int)Math.Floor(bounds.Max.X); x++)
        for (var y = (int)Math.Floor(bounds.Min.Y) - 1; y <= (int)Math.Floor(bounds.Max.Y); y++)
        for (var z = (int)Math.Floor(bounds.Min.Z); z <= (int)Math.Floor(bounds.Max.Z); z++)
        {
            var position = new Vector(x, y, z);
            var block = GetBlock(position);
            if (block == null)
            {
                yield return (new BoundingBox(new VectorD(x, y, z), new VectorD(x + 1, y + 1, z + 1)), position);
                continue;
            }
            if (ignoreWoodenDoors && TagsRegistry.Block.WoodenDoors.Entries.Contains(block.RegistryId))
                continue;

            foreach (var shape in BlockCollisionShapes.Get(level, position, block))
                yield return (shape.OffsetBy(new VectorD(x, y, z)), position);
        }
    }

    /// <summary>
    /// Adds the collision shapes near <paramref name="bounds"/> to <paramref name="shapes"/>, as
    /// <see cref="GetCollisions"/> returns them, for callers that reuse a list.
    /// </summary>
    public void CollectCollisions(BoundingBox bounds, List<BoundingBox> shapes)
    {
        for (var x = (int)Math.Floor(bounds.Min.X); x <= (int)Math.Floor(bounds.Max.X); x++)
        for (var y = (int)Math.Floor(bounds.Min.Y) - 1; y <= (int)Math.Floor(bounds.Max.Y); y++)
        for (var z = (int)Math.Floor(bounds.Min.Z); z <= (int)Math.Floor(bounds.Max.Z); z++)
        {
            var position = new Vector(x, y, z);
            if (GetBlock(position) is not { } block)
            {
                shapes.Add(new BoundingBox(new VectorD(x, y, z), new VectorD(x + 1, y + 1, z + 1)));
                continue;
            }

            foreach (var shape in BlockCollisionShapes.Get(level, position, block))
                shapes.Add(shape.OffsetBy(new VectorD(x, y, z)));
        }
    }

    // GetCollisions(bounds).Any(overlapping), without its iterators: mobs' pathfinding asks this many times a tick.
    public bool IsFree(BoundingBox bounds, bool ignoreWoodenDoors = false)
    {
        for (var x = (int)Math.Floor(bounds.Min.X); x <= (int)Math.Floor(bounds.Max.X); x++)
        for (var y = (int)Math.Floor(bounds.Min.Y) - 1; y <= (int)Math.Floor(bounds.Max.Y); y++)
        for (var z = (int)Math.Floor(bounds.Min.Z); z <= (int)Math.Floor(bounds.Max.Z); z++)
        {
            var position = new Vector(x, y, z);
            if (GetBlock(position) is not { } block)
            {
                if (Overlaps(bounds, new BoundingBox(new VectorD(x, y, z), new VectorD(x + 1, y + 1, z + 1))))
                    return false;
                continue;
            }
            if (ignoreWoodenDoors && TagsRegistry.Block.WoodenDoors.Entries.Contains(block.RegistryId))
                continue;

            foreach (var shape in BlockCollisionShapes.Get(level, position, block))
            {
                if (Overlaps(bounds, shape.OffsetBy(new VectorD(x, y, z))))
                    return false;
            }
        }
        return true;
    }

    public bool ContainsSuffocatingBlock(BoundingBox bounds)
    {
        for (var x = (int)Math.Floor(bounds.Min.X); x <= (int)Math.Floor(bounds.Max.X); x++)
        for (var y = (int)Math.Floor(bounds.Min.Y); y <= (int)Math.Floor(bounds.Max.Y); y++)
        for (var z = (int)Math.Floor(bounds.Min.Z); z <= (int)Math.Floor(bounds.Max.Z); z++)
        {
            var block = GetBlock(new Vector(x, y, z));
            if (block != null && BlockPhysics.Suffocates(block.GetHashCode()) &&
                BlockCollisionShapes.Get(level, new Vector(x, y, z), block)
                    .Any(shape => Overlaps(bounds, shape.OffsetBy(new VectorD(x, y, z)))))
                return true;
        }
        return false;
    }

    public bool IsFreeOfFluid(BoundingBox bounds)
    {
        for (var x = (int)Math.Floor(bounds.Min.X); x < (int)Math.Ceiling(bounds.Max.X); x++)
        for (var y = (int)Math.Floor(bounds.Min.Y); y < (int)Math.Ceiling(bounds.Max.Y); y++)
        for (var z = (int)Math.Floor(bounds.Min.Z); z < (int)Math.Ceiling(bounds.Max.Z); z++)
        {
            if (GetBlock(new Vector(x, y, z)) is not { } block || block.GetFluid() != FluidKind.Empty)
                return false;
        }
        return true;
    }

    public static bool Overlaps(BoundingBox left, BoundingBox right) =>
        left.Max.X > right.Min.X + 0.00001f && left.Min.X < right.Max.X - 0.00001f &&
        left.Max.Y > right.Min.Y + 0.00001f && left.Min.Y < right.Max.Y - 0.00001f &&
        left.Max.Z > right.Min.Z + 0.00001f && left.Min.Z < right.Max.Z - 0.00001f;

    public bool HasLineOfSight(VectorD start, VectorD end)
    {
        var direction = end - start;
        var target = (Vector)end.Floor();

        foreach (var cell in CellsAlong(start, end))
        {
            var block = GetBlock(cell);
            if (block == null)
                return false;

            foreach (var shape in BlockCollisionShapes.Get(level, cell, block))
            {
                if (IntersectsSegment(shape.OffsetBy((VectorD)cell), start, direction))
                    return false;
            }

            if (cell == target)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Like vanilla's <c>Level.clip</c> with outline shapes and no fluids, which is what a player aims at: the first
    /// block whose outline shape the segment from <paramref name="start"/> to <paramref name="end"/> crosses, or
    /// <c>null</c> when it crosses none before it ends or reaches an unloaded chunk.
    /// </summary>
    public (Vector Position, IBlock Block)? ClipOutlines(VectorD start, VectorD end)
    {
        return this.RaycastOutlines(start, end) is API.Blocks.BlockShapeHit hit
            && this.GetBlock(hit.Position) is IBlock block ? (hit.Position, block) : null;
    }

    /// <summary>Returns the nearest outline box hit and the location within its block.</summary>
    public API.Blocks.BlockShapeHit? RaycastOutlines(VectorD start, VectorD end)
    {
        var direction = end - start;

        if (!double.IsFinite(direction.X + direction.Y + direction.Z + start.X + start.Y + start.Z)
            || direction.MagnitudeSquared() < 1e-12)
        {
            return null;
        }

        foreach (var cell in CellsAlong(start, end))
        {
            if (GetBlock(cell) is not IBlock block)
                return null;

            var shapes = BlockEntityShapes.Get(level, cell, block);
            IReadOnlyList<BoundingBox> boxes;
            var offset = VectorD.Zero;
            if (shapes is not null)
            {
                boxes = shapes.Outline;
            }
            else
            {
                var state = block.GetHashCode();
                var (offsetX, offsetY, offsetZ) = BlockPhysics.ShapeOffset(state, cell.X, cell.Z);
                offset = new VectorD(offsetX, offsetY, offsetZ);
                boxes = BlockPhysics.OutlineBoxes(state)
                    .Select(box => new BoundingBox(new VectorD(box[0], box[1], box[2]),
                        new VectorD(box[3], box[4], box[5])))
                    .ToArray();
            }

            var hit = BlockShapeRaycast.Clip(boxes, cell, offset, start, direction);
            if (hit is not null)
                return hit.Value.Hit;
        }

        return null;
    }

    // The cells the segment from start to end passes through, in order, from start's to end's: a voxel walk that
    // skips none.
    internal static IEnumerable<Vector> CellsAlong(VectorD start, VectorD end)
    {
        var direction = end - start;
        var cell = (Vector)start.Floor();
        var target = (Vector)end.Floor();
        var stepX = Math.Sign(direction.X);
        var stepY = Math.Sign(direction.Y);
        var stepZ = Math.Sign(direction.Z);
        var deltaX = stepX == 0 ? float.PositiveInfinity : Math.Abs(1 / direction.X);
        var deltaY = stepY == 0 ? float.PositiveInfinity : Math.Abs(1 / direction.Y);
        var deltaZ = stepZ == 0 ? float.PositiveInfinity : Math.Abs(1 / direction.Z);
        var nextX = stepX == 0 ? float.PositiveInfinity : (cell.X + (stepX > 0 ? 1 : 0) - start.X) / direction.X;
        var nextY = stepY == 0 ? float.PositiveInfinity : (cell.Y + (stepY > 0 ? 1 : 0) - start.Y) / direction.Y;
        var nextZ = stepZ == 0 ? float.PositiveInfinity : (cell.Z + (stepZ > 0 ? 1 : 0) - start.Z) / direction.Z;

        var maxSteps = Math.Abs(target.X - cell.X) + Math.Abs(target.Y - cell.Y) + Math.Abs(target.Z - cell.Z) + 4;
        for (var i = 0; i < maxSteps; i++)
        {
            yield return cell;

            if (cell == target)
                yield break;

            if (nextX <= nextY && nextX <= nextZ)
            {
                cell.X += stepX;
                nextX += deltaX;
            }
            else if (nextY <= nextZ)
            {
                cell.Y += stepY;
                nextY += deltaY;
            }
            else
            {
                cell.Z += stepZ;
                nextZ += deltaZ;
            }
        }
    }

    /// <summary>Whether the segment from <paramref name="start"/> along <paramref name="direction"/> touches the box, starting inside counting.</summary>
    internal static bool SegmentIntersects(BoundingBox box, VectorD start, VectorD direction) =>
        ClipSegment(box, start, direction, out _);

    private static bool IntersectsSegment(BoundingBox box, VectorD start, VectorD direction) =>
        SegmentIntersects(box, start, direction);

    // Like vanilla AABB.clip, only an entry strictly after the start counts: a ray that starts inside or on the
    // box (e.g. a projectile that was just deflected off it) does not hit it again.
    internal static double? RayIntersection(BoundingBox box, VectorD start, VectorD direction) =>
        ClipSegment(box, start, direction, out var entry) && entry > 0 ? entry : null;

    private static bool ClipSegment(BoundingBox box, VectorD start, VectorD direction, out double min)
    {
        // The segment is [0, 1]: an entry before the start clamps to it, and a box wholly behind the start misses.
        min = 0d;
        var max = 1d;
        return ClipRay(start.X, direction.X, box.Min.X, box.Max.X, ref min, ref max) &&
            ClipRay(start.Y, direction.Y, box.Min.Y, box.Max.Y, ref min, ref max) &&
            ClipRay(start.Z, direction.Z, box.Min.Z, box.Max.Z, ref min, ref max);
    }

    private static bool ClipRay(double start, double direction, double lower, double upper, ref double min, ref double max)
    {
        if (Math.Abs(direction) < 0.000001f)
            return start >= lower && start <= upper;

        var first = (lower - start) / direction;
        var second = (upper - start) / direction;
        min = Math.Max(min, Math.Min(first, second));
        max = Math.Min(max, Math.Max(first, second));
        return min <= max;
    }
}
