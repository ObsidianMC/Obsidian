using Obsidian.API.World;
using Obsidian.API.Inventory;
using System.Threading;

namespace Obsidian.WorldData.Portals;

internal sealed class LevelPortals(AbstractLevel level)
{
    private readonly SemaphoreSlim changes = new(1, 1);
    private readonly ConcurrentQueue<Vector> neighborChanges = new();

    public void OnBlockChanged(Vector position) => this.neighborChanges.Enqueue(position);

    /// <summary>
    /// The registered portal type the portal block <paramref name="block"/> at <paramref name="position"/> belongs
    /// to, or null when it's inert (see <see cref="PortalOwnership"/>).
    /// </summary>
    public PortalType? TypeAt(Vector position, IBlock block) =>
        PortalOwnership.TypeAt(this.ChunkAt(position), position, block);

    private IChunk? ChunkAt(Vector position) => level.GetLoadedChunk(position.X >> 4, position.Z >> 4);

    public async Task TickAsync()
    {
        if (!await this.changes.WaitAsync(0))
            return;
        try
        {
            var checkedBlocks = new HashSet<Vector>();
            while (this.neighborChanges.TryDequeue(out var changed))
            {
                var changedBlock = await level.GetBlockAsync(changed);
                PortalOwnership.Release(this.ChunkAt(changed), changed, changedBlock);
                if (changedBlock?.RegistryId is BlockIds.Fire or BlockIds.SoulFire)
                    await this.TryIgniteCoreAsync(changed, fire: true);
                foreach (var direction in Vector.AllDirections)
                {
                    var position = changed + direction;
                    var block = await level.GetBlockAsync(position);
                    if (block is null || this.TypeAt(position, block) is not PortalType type
                        || !PortalTypesRegistry.TryEnter(type, out var properties, out var call))
                        continue;

                    using var behavior = call;
                    var axis = block.GetProperty("axis") ?? "x";
                    // Vanilla ignores changes normal to the portal plane.
                    if ((axis == "x" && direction.Z != 0) || (axis == "z" && direction.X != 0))
                        continue;
                    if (!checkedBlocks.Add(position))
                        continue;
                    var shape = await PortalFrame.FindAsync(
                        level.GetBlockAsync, position, axis, level.MinY, properties);
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

    public async ValueTask<bool> TryIgniteAsync(
        Vector position, PortalType? type = null, ItemStack? igniter = null)
    {
        await this.changes.WaitAsync();
        try
        {
            return await this.TryIgniteCoreAsync(position, type, igniter);
        }
        finally
        {
            this.changes.Release();
        }
    }

    private async ValueTask<bool> TryIgniteCoreAsync(
        Vector position, PortalType? requested = null, ItemStack? igniter = null, bool fire = false)
    {
        foreach (var type in requested is not null ? new[] { requested } : PortalTypesRegistry.ActiveTypes())
        {
            if (!PortalTypesRegistry.TryEnter(type, out var properties, out var call))
                continue;

            using var behavior = call;
            if (fire && !properties.IgnitesFromFire)
                continue;

            if (igniter is not null && !(properties.Igniter is Item item && igniter.Is(item))
                && !(type == PortalTypesRegistry.Nether
                    && igniter.Holder.Id is ItemIds.FlintAndSteel or ItemIds.FireCharge))
                continue;

            if (properties.ResolveTarget(level) is null)
                continue;

            foreach (var axis in new[] { "x", "z" })
            {
                var shape = await PortalFrame.FindAsync(level.GetBlockAsync, position, axis, level.MinY, properties);
                if (shape is not PortalFrame frame || frame.PortalBlocks != 0)
                    continue;

                await this.FillAsync(frame, type, properties);
                return true;
            }
        }

        return false;
    }

    internal bool HasIgniter(ItemStack stack)
    {
        foreach (var type in PortalTypesRegistry.ActiveTypes())
        {
            if (!PortalTypesRegistry.TryEnter(type, out var properties, out var call))
                continue;

            using var behavior = call;
            if (properties.Igniter is Item item && stack.Is(item))
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
            if (frame?.RegistryId != BlockIds.EndPortalFrame || frame.GetProperty("eye") != "false")
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
                        await level.SetBlockAsync(origin + new Vector(x, 0, z), BlocksRegistry.EndPortal, true);
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

    /// <summary>
    /// Finds the nearest portal block of <paramref name="type"/> around <paramref name="target"/>, skipping portal
    /// blocks of other types sharing its block, or builds a new exit when there is none.
    /// </summary>
    public async Task<PortalFrame?> FindOrCreateExitAsync(
        Vector target, string axis, Func<bool> canContinue, PortalType type, PortalProperties properties)
    {
        var radius = properties.SearchRadius?.Invoke(level) ?? 128;
        if (radius is < 1 or > 128)
            throw new InvalidOperationException("Portal search radius must be between 1 and 128 blocks.");
        // Existing portals live in saved or loaded chunks; searching must not generate the entire lookup area.
        using var area = await PortalArea.LoadAsync(level, target, radius + PortalFrame.MaxSize, canContinue,
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
                foreach (var position in PortalBlocks(chunk, properties.PortalBlock.RegistryId))
                {
                    if (Math.Abs(position.X - target.X) > radius || Math.Abs(position.Z - target.Z) > radius ||
                        !InsideBorder(position)
                        || PortalOwnership.TypeAt(chunk, position, area.Read(position)) != type)
                        continue;
                    var distance = DistanceSquared(position, target);
                    if (distance < nearestDistance || distance == nearestDistance && position.Y < nearest?.Y)
                    {
                        nearest = position;
                        nearestDistance = distance;
                    }
                }
            }
            if (nearest is Vector found)
            {
                var existingAxis = area.Read(found).GetProperty("axis") ?? "x";
                var frame = await PortalFrame.FindAsync(area.ReadAsync, found, existingAxis, level.MinY, properties);
                if (frame is { IsComplete: true })
                    return frame;
                // Command-placed portal blocks still work, even without a frame.
                return await RectangleAsync(area.ReadAsync, found, existingAxis, properties);
            }
            using var creationArea = await PortalArea.LoadAsync(level, target, 16 + PortalFrame.MaxSize, canContinue);
            return creationArea is null
                ? null
                : await this.CreateExitAsync(creationArea, target, axis, type, properties);
        }
        finally
        {
            this.changes.Release();
        }
    }

    private async Task<PortalFrame?> CreateExitAsync(
        PortalArea area, Vector target, string axis, PortalType type, PortalProperties properties)
    {
        var step = axis == "x" ? new Vector(1, 0, 0) : new Vector(0, 0, 1);
        var normal = axis == "x" ? new Vector(0, 0, 1) : new Vector(1, 0, 0);
        // Vanilla's output-affecting site search: radius 16, minimum frame, three-wide clearance,
        // then a safety platform at Y >= 70 with nine blocks of headroom when no natural site exists.
        var width = properties.MinWidth;
        var height = properties.MinHeight;
        var top = Math.Min(level.MinY + level.Height - 1, level.MinY + level.DimensionType.Element.LogicalHeight);
        Vector? best = null;
        Vector? fallback = null;
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
                if (y + height + 1 > top
                    || ceiling - y > 0 && ceiling - y < height
                    || !CanHost(area, origin, step, normal, 0, width, height))
                    continue;
                var distance = DistanceSquared(origin, target);
                if (CanHost(area, origin, step, normal, -1, width, height)
                    && CanHost(area, origin, step, normal, 1, width, height)
                    && distance < bestDistance)
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
            var max = top - Math.Max(9, height + 1);
            if (max < min)
                return null;
            var origin = target - step;
            origin.Y = Math.Clamp(origin.Y, min, max);
            best = origin;
            for (var side = -1; side <= 1; side++)
                for (var x = 0; x < width; x++)
                    for (var y = -1; y < height; y++)
                        await level.SetBlockAsync(origin + step * x + normal * side + Vector.Up * y,
                            y < 0 ? properties.FrameBlock : BlocksRegistry.Air);
        }
        var frame = new PortalFrame(best.Value, axis, width, height, width * height);
        for (var x = -1; x < width + 1; x++)
            for (var y = -1; y < height + 1; y++)
                if (x == -1 || x == width || y == -1 || y == height)
                    await level.SetBlockAsync(
                        frame.Origin + step * x + Vector.Up * y, properties.FrameBlock, true);
        await this.FillAsync(frame, type, properties);
        return frame;
    }

    // Claims each block for the type, so types sharing a portal block keep their own portals.
    private async Task FillAsync(PortalFrame frame, PortalType type, PortalProperties properties)
    {
        var portal = properties.PortalBlock.WithProperty("axis", frame.Axis);
        foreach (var position in frame.Interior())
        {
            await level.SetBlockAsync(position, portal);
            PortalOwnership.Claim(level, position, type, portal);
        }
    }

    private static bool CanHost(
        PortalArea area, Vector origin, Vector step, Vector normal, int side, int width, int height)
    {
        for (var x = -1; x < width + 1; x++)
            for (var y = -1; y < height + 1; y++)
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
    internal static bool InsideBorder(Vector position) =>
        position.X >= -WorldBorderLimit && position.X < WorldBorderLimit
        && position.Z >= -WorldBorderLimit && position.Z < WorldBorderLimit;
    private static double DistanceSquared(Vector a, Vector b) =>
        Math.Pow((double)a.X - b.X, 2) + Math.Pow((double)a.Y - b.Y, 2) + Math.Pow((double)a.Z - b.Z, 2);

    private static IEnumerable<Vector> PortalBlocks(IChunk chunk, int portalId)
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
                .Any(index => palette.GetValueFromIndex(index)?.RegistryId == portalId))
                continue;
            for (var y = 0; y < 16; y++)
                for (var z = 0; z < 16; z++)
                    for (var x = 0; x < 16; x++)
                        if (section.GetBlock(x, y, z).RegistryId == portalId)
                            yield return new Vector(
                                (chunk.X << 4) + x, chunk.MinY + sectionIndex * 16 + y, (chunk.Z << 4) + z);
        }
    }

    internal static async ValueTask<PortalFrame> RectangleAsync(
        Func<Vector, ValueTask<IBlock?>> read, Vector position, string axis, PortalProperties? properties = null)
    {
        var step = axis == "x" ? new Vector(1, 0, 0) : new Vector(0, 0, 1);
        properties ??= PortalFrame.NetherProperties;
        async ValueTask<bool> Matches(Vector p) =>
            await read(p) is IBlock block && block.RegistryId == properties.PortalBlock.RegistryId
                && block.GetProperty("axis") == axis;
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
