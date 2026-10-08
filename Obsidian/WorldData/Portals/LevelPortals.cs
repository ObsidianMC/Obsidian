using System.Threading;

namespace Obsidian.WorldData.Portals;

internal sealed class LevelPortals(AbstractLevel level)
{
    private readonly SemaphoreSlim changes = new(1, 1);
    private readonly ConcurrentQueue<Vector> neighborChanges = new();

    public void OnBlockChanged(Vector position) => this.neighborChanges.Enqueue(position);

    public async Task TickAsync()
    {
        if (!await this.changes.WaitAsync(0))
            return;
        try
        {
            var checkedBlocks = new HashSet<Vector>();
            while (this.neighborChanges.TryDequeue(out var changed))
            {
                if ((await level.GetBlockAsync(changed))?.Material is Material.Fire or Material.SoulFire)
                    await this.TryIgniteCoreAsync(changed);
                foreach (var direction in Vector.AllDirections)
                {
                    var position = changed + direction;
                    var block = await level.GetBlockAsync(position);
                    if (block?.Material != Material.NetherPortal)
                        continue;
                    var axis = block.GetProperty("axis") ?? "x";
                    // Vanilla ignores changes normal to the portal plane.
                    if ((axis == "x" && direction.Z != 0) || (axis == "z" && direction.X != 0))
                        continue;
                    if (!checkedBlocks.Add(position))
                        continue;
                    var shape = await NetherPortalShape.FindAsync(level.GetBlockAsync, position, axis, level.MinY);
                    if (shape is { IsComplete: true })
                        continue;
                    await level.SetBlockAsync(position, BlocksRegistry.Air);
                    this.neighborChanges.Enqueue(position);
                }
            }
        }
        finally
        {
            this.changes.Release();
        }
    }

    public async ValueTask<bool> TryIgniteAsync(Vector position)
    {
        if (level.DimensionName is not ("minecraft:overworld" or "minecraft:the_nether"))
            return false;
        await this.changes.WaitAsync();
        try
        {
            return await this.TryIgniteCoreAsync(position);
        }
        finally
        {
            this.changes.Release();
        }
    }

    private async ValueTask<bool> TryIgniteCoreAsync(Vector position)
    {
        if (level.DimensionName is not ("minecraft:overworld" or "minecraft:the_nether"))
            return false;
        foreach (var axis in new[] { "x", "z" })
        {
            var shape = await NetherPortalShape.FindAsync(level.GetBlockAsync, position, axis, level.MinY);
            if (shape is not NetherPortalShape frame || frame.PortalBlocks != 0)
                continue;
            await this.FillAsync(frame);
            return true;
        }
        return false;
    }

    public async ValueTask<bool> TryInsertEyeAsync(Vector position)
    {
        await this.changes.WaitAsync();
        try
        {
            var frame = await level.GetBlockAsync(position);
            if (frame?.Material != Material.EndPortalFrame || frame.GetProperty("eye") != "false")
                return false;
            await level.SetBlockAsync(position, frame.WithProperty("eye", true), true);
            level.BroadcastLevelEvent(1503, position, 0);
            // Each of the twelve positions in a ring can be the final frame filled.
            foreach (var (offset, _) in EndPortalFrames.Ring(Vector.Zero))
            {
                var origin = position - offset;
                if (!await EndPortalFrames.IsCompleteAsync(level.GetBlockAsync, origin))
                    continue;
                for (var x = 0; x < 3; x++)
                    for (var z = 0; z < 3; z++)
                        await level.SetBlockAsync(origin + new Vector(x, 0, z), BlocksRegistry.Get(Material.EndPortal), true);
                level.BroadcastLevelEvent(1038, origin + new Vector(1, 0, 1), 0);
                break;
            }
            return true;
        }
        finally
        {
            this.changes.Release();
        }
    }

    public async Task<NetherPortalShape?> FindOrCreateExitAsync(Vector target, string axis, Func<bool> canContinue)
    {
        var radius = level.DimensionName == "minecraft:the_nether" ? 16 : 128;
        // Existing portals live in saved or loaded chunks; searching must not generate the entire lookup area.
        using var area = await PortalArea.LoadAsync(level, target, radius + NetherPortalShape.MaxSize, canContinue,
            generateMissing: false);
        if (area is null)
            return null;

        await this.changes.WaitAsync();
        try
        {
            if (!canContinue())
                return null;
            Vector? nearest = null;
            var nearestDistance = double.MaxValue;
            foreach (var chunk in area.Chunks.Values)
            {
                foreach (var position in PortalBlocks(chunk))
                {
                    if (Math.Abs(position.X - target.X) > radius || Math.Abs(position.Z - target.Z) > radius ||
                        !InsideBorder(position))
                        continue;
                    var distance = DistanceSquared(position, target);
                    if (distance < nearestDistance || distance == nearestDistance && position.Y < nearest?.Y)
                    {
                        nearest = position;
                        nearestDistance = distance;
                    }
                }
            }
            if (nearest is { } found)
            {
                var existingAxis = area.Read(found).GetProperty("axis") ?? "x";
                var frame = await NetherPortalShape.FindAsync(area.ReadAsync, found, existingAxis, level.MinY);
                if (frame is { IsComplete: true })
                    return frame;
                // Command-placed portal blocks still work, even without a frame.
                return await RectangleAsync(area.ReadAsync, found, existingAxis);
            }
            using var creationArea = await PortalArea.LoadAsync(level, target, 16 + NetherPortalShape.MaxSize, canContinue);
            return creationArea is null ? null : await this.CreateExitAsync(creationArea, target, axis);
        }
        finally
        {
            this.changes.Release();
        }
    }

    private async Task<NetherPortalShape?> CreateExitAsync(PortalArea area, Vector target, string axis)
    {
        var step = axis == "x" ? new Vector(1, 0, 0) : new Vector(0, 0, 1);
        var normal = axis == "x" ? new Vector(0, 0, 1) : new Vector(1, 0, 0);
        CodecRegistry.TryGetDimension(level.DimensionName, out var codec);
        var top = Math.Min(level.MinY + level.Height - 1, level.MinY + (codec?.Element.LogicalHeight ?? level.Height));
        Vector? best = null, fallback = null;
        var bestDistance = double.MaxValue;
        var fallbackDistance = double.MaxValue;
        foreach (var offset in Spiral(16))
        {
            var column = target + offset;
            if (!InsideBorder(column) || !InsideBorder(column + step))
                continue;
            var surface = area.Chunks[(column.X >> 4, column.Z >> 4)].Heightmaps[HeightmapType.MotionBlocking]
                .GetHeight(NumericsHelper.Modulo(column.X, 16), NumericsHelper.Modulo(column.Z, 16));
            var origin = column - step;
            for (var y = Math.Min(top, surface); y >= level.MinY; y--)
            {
                origin.Y = y;
                if (!IsFree(area.Read(origin)))
                    continue;
                var ceiling = y;
                while (y > level.MinY && IsFree(area.Read(origin + Vector.Down)))
                    origin.Y = --y;
                if (y + 4 > top || ceiling - y is > 0 and < 3 || !CanHost(area, origin, step, normal, 0))
                    continue;
                var distance = DistanceSquared(origin, target);
                if (CanHost(area, origin, step, normal, -1) && CanHost(area, origin, step, normal, 1) && distance < bestDistance)
                {
                    best = origin;
                    bestDistance = distance;
                }
                if (best is null && distance < fallbackDistance)
                {
                    fallback = origin;
                    fallbackDistance = distance;
                }
            }
        }
        best ??= fallback;
        if (best is null)
        {
            var min = Math.Max(level.MinY + 1, 70);
            var max = top - 9;
            if (max < min)
                return null;
            var origin = target - step;
            origin.Y = Math.Clamp(origin.Y, min, max);
            best = origin;
            for (var side = -1; side <= 1; side++)
                for (var x = 0; x < 2; x++)
                    for (var y = -1; y < 3; y++)
                        await level.SetBlockAsync(origin + step * x + normal * side + Vector.Up * y,
                            y < 0 ? BlocksRegistry.Get(Material.Obsidian) : BlocksRegistry.Air);
        }
        var frame = new NetherPortalShape(best.Value, axis, 2, 3, 6);
        for (var x = -1; x < 3; x++)
            for (var y = -1; y < 4; y++)
                if (x is -1 or 2 || y is -1 or 3)
                    await level.SetBlockAsync(frame.Origin + step * x + Vector.Up * y, BlocksRegistry.Get(Material.Obsidian), true);
        await this.FillAsync(frame);
        return frame;
    }

    private async Task FillAsync(NetherPortalShape frame)
    {
        var portal = BlocksRegistry.Get(Material.NetherPortal).WithProperty("axis", frame.Axis);
        foreach (var position in frame.Interior())
            await level.SetBlockAsync(position, portal);
    }

    private static bool CanHost(PortalArea area, Vector origin, Vector step, Vector normal, int side)
    {
        for (var x = -1; x < 3; x++)
            for (var y = -1; y < 4; y++)
            {
                var block = area.Read(origin + step * x + normal * side + Vector.Up * y);
                if (y < 0 ? !block.IsSolid() : !IsFree(block))
                    return false;
            }
        return true;
    }

    private static bool IsFree(IBlock block) => block.CanBeReplaced() && FluidStateEmpty(block);
    private static bool FluidStateEmpty(IBlock block) => !block.IsLiquid && block.GetProperty("waterlogged") != "true";
    internal const int WorldBorderLimit = 29999984;
    internal static bool InsideBorder(Vector position) => position.X >= -WorldBorderLimit && position.X < WorldBorderLimit &&
        position.Z >= -WorldBorderLimit && position.Z < WorldBorderLimit;
    private static double DistanceSquared(Vector a, Vector b) =>
        Math.Pow((double)a.X - b.X, 2) + Math.Pow((double)a.Y - b.Y, 2) + Math.Pow((double)a.Z - b.Z, 2);

    private static IEnumerable<Vector> PortalBlocks(IChunk chunk)
    {
        // Skip sections whose local palette has never contained a portal.
        var sections = chunk.Sections.ToArray();
        for (var sectionIndex = 0; sectionIndex < sections.Length; sectionIndex++)
        {
            var section = sections[sectionIndex];
            if (section.IsEmpty)
                continue;
            var palette = section.BlockStateContainer.Palette;
            if (palette.BitCount <= 8 && !Enumerable.Range(0, palette.Count)
                .Any(index => palette.GetValueFromIndex(index)?.Material == Material.NetherPortal))
                continue;
            for (var y = 0; y < 16; y++)
                for (var z = 0; z < 16; z++)
                    for (var x = 0; x < 16; x++)
                        if (section.GetBlock(x, y, z).Material == Material.NetherPortal)
                            yield return new Vector((chunk.X << 4) + x, chunk.MinY + sectionIndex * 16 + y, (chunk.Z << 4) + z);
        }
    }

    internal static async ValueTask<NetherPortalShape> RectangleAsync(Func<Vector, ValueTask<IBlock?>> read, Vector position, string axis)
    {
        var step = axis == "x" ? new Vector(1, 0, 0) : new Vector(0, 0, 1);
        async ValueTask<bool> Matches(Vector p) => (await read(p)) is { Material: Material.NetherPortal } block && block.GetProperty("axis") == axis;
        for (var i = 0; i < 20 && await Matches(position + Vector.Down); i++)
            position += Vector.Down;
        for (var i = 0; i < 20 && await Matches(position - step); i++)
            position -= step;
        var width = 1;
        while (width < 21 && await Matches(position + step * width))
            width++;
        var height = 1;
        while (height < 21)
        {
            var complete = true;
            for (var x = 0; x < width; x++)
                complete &= await Matches(position + step * x + Vector.Up * height);
            if (!complete)
                break;
            height++;
        }
        return new(position, axis, width, height, width * height);
    }

    private static IEnumerable<Vector> Spiral(int radius)
    {
        yield return Vector.Zero;
        var position = Vector.Zero;
        Vector[] directions = [new(1, 0, 0), new(0, 0, 1), new(-1, 0, 0), new(0, 0, -1)];
        var direction = 0;
        for (var length = 1; length <= radius * 2 + 1; length++)
            for (var side = 0; side < 2; side++)
            {
                for (var i = 0; i < length; i++)
                {
                    position += directions[direction % 4];
                    if (Math.Abs(position.X) <= radius && Math.Abs(position.Z) <= radius)
                        yield return position;
                }
                direction++;
            }
    }
}
