using Obsidian.WorldData;

namespace Obsidian.Entities.AI;

internal sealed class MobTerrain(ILevel level)
{
    public int GetSkyLight(Vector position) => level is AbstractLevel concrete && position.Y is >= -64 and < 320 ?
        concrete.GetLoadedChunk(position.X >> 4, position.Z >> 4)?.GetLightLevel(position.X, position.Y, position.Z, LightType.Sky) ?? 0 : 0;

    public float GetFriction(VectorF position) => GetBlock((Vector)(position - new VectorF(0, 0.500001f, 0)).Floor())?.Material switch
    {
        Material.Ice or Material.PackedIce or Material.FrostedIce => 0.98f,
        Material.BlueIce => 0.989f,
        Material.SlimeBlock => 0.8f,
        _ => 0.6f
    };
    public IBlock? GetBlock(Vector position)
    {
        if (level is not AbstractLevel concrete || position.Y is < -64 or >= 320)
            return null;

        return concrete.GetLoadedChunk(position.X >> 4, position.Z >> 4)?.GetBlock(position);
    }

    public IEnumerable<BoundingBox> GetCollisions(BoundingBox bounds, bool ignoreWoodenDoors = false)
    {
        // Shapes such as fences can extend above their block's unit cube.
        for (var x = (int)MathF.Floor(bounds.Min.X); x <= (int)MathF.Floor(bounds.Max.X); x++)
        for (var y = (int)MathF.Floor(bounds.Min.Y) - 1; y <= (int)MathF.Floor(bounds.Max.Y); y++)
        for (var z = (int)MathF.Floor(bounds.Min.Z); z <= (int)MathF.Floor(bounds.Max.Z); z++)
        {
            var position = new Vector(x, y, z);
            var block = GetBlock(position);
            if (block == null)
            {
                yield return new BoundingBox(new VectorF(x, y, z), new VectorF(x + 1, y + 1, z + 1));
                continue;
            }
            if (ignoreWoodenDoors && TagsRegistry.Block.WoodenDoors.Entries.Contains(block.RegistryId))
                continue;

            foreach (var shape in BlockCollisionShapes.Get(block))
                yield return shape.OffsetBy(new VectorF(x, y, z));
        }
    }

    public bool IsFree(BoundingBox bounds, bool ignoreWoodenDoors = false) =>
        !GetCollisions(bounds, ignoreWoodenDoors).Any(shape => Overlaps(bounds, shape));

    public static bool Overlaps(BoundingBox left, BoundingBox right) =>
        left.Max.X > right.Min.X + 0.00001f && left.Min.X < right.Max.X - 0.00001f &&
        left.Max.Y > right.Min.Y + 0.00001f && left.Min.Y < right.Max.Y - 0.00001f &&
        left.Max.Z > right.Min.Z + 0.00001f && left.Min.Z < right.Max.Z - 0.00001f;

    public bool HasLineOfSight(VectorF start, VectorF end)
    {
        var direction = end - start;
        var cell = (Vector)start.Floor();
        var target = (Vector)end.Floor();
        var stepX = Math.Sign(direction.X);
        var stepY = Math.Sign(direction.Y);
        var stepZ = Math.Sign(direction.Z);
        var deltaX = stepX == 0 ? float.PositiveInfinity : MathF.Abs(1 / direction.X);
        var deltaY = stepY == 0 ? float.PositiveInfinity : MathF.Abs(1 / direction.Y);
        var deltaZ = stepZ == 0 ? float.PositiveInfinity : MathF.Abs(1 / direction.Z);
        var nextX = stepX == 0 ? float.PositiveInfinity : (cell.X + (stepX > 0 ? 1 : 0) - start.X) / direction.X;
        var nextY = stepY == 0 ? float.PositiveInfinity : (cell.Y + (stepY > 0 ? 1 : 0) - start.Y) / direction.Y;
        var nextZ = stepZ == 0 ? float.PositiveInfinity : (cell.Z + (stepZ > 0 ? 1 : 0) - start.Z) / direction.Z;

        var maxSteps = Math.Abs(target.X - cell.X) + Math.Abs(target.Y - cell.Y) + Math.Abs(target.Z - cell.Z) + 4;
        for (var i = 0; i < maxSteps; i++)
        {
            var block = GetBlock(cell);
            if (block == null)
                return false;

            foreach (var shape in BlockCollisionShapes.Get(block))
            {
                if (IntersectsSegment(shape.OffsetBy((VectorF)cell), start, direction))
                    return false;
            }

            if (cell == target)
                return true;

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

        return false;
    }

    private static bool IntersectsSegment(BoundingBox box, VectorF start, VectorF direction)
    {
        var min = 0f;
        var max = 1f;
        return ClipRay(start.X, direction.X, box.Min.X, box.Max.X, ref min, ref max) &&
            ClipRay(start.Y, direction.Y, box.Min.Y, box.Max.Y, ref min, ref max) &&
            ClipRay(start.Z, direction.Z, box.Min.Z, box.Max.Z, ref min, ref max);
    }

    private static bool ClipRay(float start, float direction, float lower, float upper, ref float min, ref float max)
    {
        if (MathF.Abs(direction) < 0.000001f)
            return start >= lower && start <= upper;

        var first = (lower - start) / direction;
        var second = (upper - start) / direction;
        min = MathF.Max(min, MathF.Min(first, second));
        max = MathF.Min(max, MathF.Max(first, second));
        return min <= max;
    }
}
