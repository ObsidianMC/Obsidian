using Obsidian.API;
using Obsidian.API.World;
using Obsidian.API.World.Generator;

namespace Obsidian.Builtins.WorldEdit;

internal sealed class EditSession
{
    internal const int HistoryLimit = 10;

    private ILevel? selectionLevel;
    private Vector? first;
    private Vector? second;
    internal Dictionary<Vector, BlockSnapshot>? Clipboard { get; private set; }
    internal List<EditHistory> UndoHistory { get; } = [];
    internal List<EditHistory> RedoHistory { get; } = [];

    internal string Select(ILevel level, Vector position, bool first)
    {
        CheckPosition(level, position);
        if (!ReferenceEquals(this.selectionLevel, level))
        {
            this.first = this.second = null;
            this.selectionLevel = level;
        }
        if (first) this.first = position;
        else this.second = position;
        return $"Position {(first ? 1 : 2)}: {position.X}, {position.Y}, {position.Z}.";
    }

    internal EditRegion Selection(ILevel level)
    {
        if (!ReferenceEquals(this.selectionLevel, level) || this.first is not { } first || this.second is not { } second)
            throw new ArgumentException("Select two positions in this dimension first (//pos1 and //pos2, or //wand).");
        var region = new EditRegion(first, second);
        region.CheckLimit();
        CheckPosition(level, region.Min);
        CheckPosition(level, region.Max);
        return region;
    }

    internal void SetSelection(ILevel level, EditRegion region)
    {
        region.CheckLimit();
        CheckPosition(level, region.Min);
        CheckPosition(level, region.Max);
        this.selectionLevel = level;
        this.first = region.Min;
        this.second = region.Max;
    }

    internal void ClearSelection()
    {
        this.first = this.second = null;
        this.selectionLevel = null;
    }

    internal static void CheckPosition(ILevel level, Vector position)
    {
        EditRegion.CheckPosition(position);
        var dimension = level.DimensionType.Element;
        if (position.Y < dimension.MinY || (long)position.Y >= (long)dimension.MinY + dimension.Height)
            throw new ArgumentException("Position is outside this dimension's build height.");
    }

    internal static Dictionary<Vector, BlockSnapshot> ReadRegion(ILevel level, EditRegion region)
    {
        var result = new Dictionary<Vector, BlockSnapshot>();
        foreach (var position in region.Positions())
        {
            CheckPosition(level, position);
            result.Add(position, level.CaptureBlock(position)
                ?? throw new ArgumentException("The entire area must be loaded before editing."));
        }
        return result;
    }

    internal int Copy(ILevel level, Vector origin)
    {
        var blocks = ReadRegion(level, this.Selection(level));
        this.Clipboard = blocks.ToDictionary(pair => pair.Key - origin, pair => pair.Value);
        return blocks.Count;
    }

    internal void TransformClipboard(Func<Vector, Vector> transform, Func<IBlock, IBlock> transformBlock)
    {
        var clipboard = this.Clipboard ?? throw new ArgumentException("Your clipboard is empty. Use //copy first.");
        this.Clipboard = clipboard.ToDictionary(pair => transform(pair.Key),
            pair => pair.Value with { Block = transformBlock(pair.Value.Block) });
    }

    internal async Task<int> PasteAsync(ILevel level, Vector origin, bool skipAir, bool select)
    {
        var clipboard = this.Clipboard ?? throw new ArgumentException("Your clipboard is empty. Use //copy first.");
        var blocks = clipboard.Where(pair => !skipAir || !pair.Value.Block.IsAir)
            .ToDictionary(pair => EditRegion.Add(origin, pair.Key), pair => pair.Value);
        var changed = await this.ApplyAsync(level, blocks);
        if (select && clipboard.Count > 0)
        {
            var positions = clipboard.Keys.Select(position => EditRegion.Add(origin, position)).ToArray();
            this.SetSelection(level, Bounds(positions));
        }
        return changed;
    }

    internal async Task<int> FillAsync(ILevel level, BlockPattern pattern, string mode, BlockMask? mask = null)
    {
        var region = this.Selection(level);
        var source = ReadRegion(level, region);
        var desired = new Dictionary<Vector, BlockSnapshot>();
        foreach (var (position, old) in source)
        {
            var side = position.X == region.Min.X || position.X == region.Max.X
                || position.Z == region.Min.Z || position.Z == region.Max.Z;
            var edge = side || position.Y == region.Min.Y || position.Y == region.Max.Y;
            if (mode == "walls" && !side || mode == "outline" && !edge || mask is not null && !mask.Matches(old.Block))
                continue;
            desired.Add(position, new BlockSnapshot(pattern.Pick()));
        }
        return await this.ApplyAsync(level, desired);
    }

    internal async Task<int> MoveAsync(ILevel level, Vector offset)
    {
        var region = this.Selection(level);
        var moved = region.Shift(offset);
        moved.CheckLimit();
        CheckPosition(level, moved.Min);
        CheckPosition(level, moved.Max);
        var source = ReadRegion(level, region);
        var air = new BlockSnapshot(BlockPattern.Resolve("air"));
        var desired = source.Keys.ToDictionary(position => position, _ => air);
        foreach (var (position, snapshot) in source)
            desired[EditRegion.Add(position, offset)] = snapshot;
        var changed = await this.ApplyAsync(level, desired);
        this.SetSelection(level, moved);
        return changed;
    }

    internal async Task<int> StackAsync(ILevel level, Vector direction, int count)
    {
        var region = this.Selection(level);
        if (count <= 0 || region.Volume * count > EditRegion.MaxBlocks)
            throw new ArgumentException($"Stack count must be positive and fit within {EditRegion.MaxBlocks:N0} blocks.");
        var source = ReadRegion(level, region);
        var size = region.Size;
        var step = new Vector(direction.X * size.X, direction.Y * size.Y, direction.Z * size.Z);
        var desired = new Dictionary<Vector, BlockSnapshot>();
        for (var copy = 1; copy <= count; copy++)
        {
            var offset = EditRegion.Scale(step, copy);
            foreach (var (position, snapshot) in source)
                desired.Add(EditRegion.Add(position, offset), snapshot);
        }
        return await this.ApplyAsync(level, desired);
    }

    internal async Task<int> ShapeAsync(ILevel level, Vector center, BlockPattern pattern,
        int radius, int height, bool cylinder, bool hollow)
    {
        if (radius < 1 || radius > EditRegion.MaxBlocks || height < 1 || height > EditRegion.MaxBlocks)
            throw new ArgumentException("Radius and height must be positive and within the edit limit.");
        var bounds = new EditRegion(EditRegion.Add(center, new Vector(-radius, cylinder ? 0 : -radius, -radius)),
            EditRegion.Add(center, new Vector(radius, cylinder ? height - 1 : radius, radius)));
        bounds.CheckLimit();
        CheckPosition(level, bounds.Min);
        CheckPosition(level, bounds.Max);
        var desired = new Dictionary<Vector, BlockSnapshot>();
        var outer = (radius + 0.5) * (radius + 0.5);
        var inner = (radius - 0.5) * (radius - 0.5);
        foreach (var position in bounds.Positions())
        {
            var offset = position - center;
            var distance = (long)offset.X * offset.X + (long)offset.Z * offset.Z
                + (cylinder ? 0 : (long)offset.Y * offset.Y);
            if (distance <= outer && (!hollow || distance >= inner))
                desired.Add(position, new BlockSnapshot(pattern.Pick()));
        }
        return await this.ApplyAsync(level, desired);
    }

    internal async Task<int> ApplyAsync(ILevel level, IReadOnlyDictionary<Vector, BlockSnapshot> desired, bool remember = true)
    {
        if (desired.Count > EditRegion.MaxBlocks)
            throw new ArgumentException($"The edit exceeds the {EditRegion.MaxBlocks:N0} block limit.");

        var planned = new List<BlockChange>(desired.Count);
        foreach (var (position, after) in desired)
        {
            CheckPosition(level, position);
            var before = level.CaptureBlock(position)
                ?? throw new ArgumentException("The entire area must be loaded before editing.");
            if (BlockPattern.StateId(before.Block) == BlockPattern.StateId(after.Block) && before.Data is null && after.Data is null)
                continue;
            planned.Add(new BlockChange(position, before, after));
        }

        var applied = new List<BlockChange>(planned.Count);
        try
        {
            foreach (var change in planned)
            {
                // Track before calling: an exception may happen after the block has already changed.
                applied.Add(change);
                if (!await level.RestoreBlockAsync(change.Position, change.After))
                {
                    applied.RemoveAt(applied.Count - 1);
                    throw new InvalidOperationException("A chunk unloaded during the edit.");
                }
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Edit interrupted after {applied.Count:N0} block(s). "
                + "Use //undo to restore the recorded changes.", ex);
        }
        finally
        {
            if (remember && applied.Count > 0)
            {
                this.UndoHistory.Add(new EditHistory(level, applied));
                if (this.UndoHistory.Count > HistoryLimit)
                    this.UndoHistory.RemoveAt(0);
                this.RedoHistory.Clear();
            }
        }
        return applied.Count;
    }

    internal async Task<int> ReplayAsync(bool redo)
    {
        var source = redo ? this.RedoHistory : this.UndoHistory;
        var destination = redo ? this.UndoHistory : this.RedoHistory;
        if (source.Count == 0)
            throw new ArgumentException(redo ? "Nothing to redo." : "Nothing to undo.");
        var entry = source[^1];
        var desired = entry.Changes.ToDictionary(change => change.Position, change => redo ? change.After : change.Before);
        var count = await this.ApplyAsync(entry.Level, desired, remember: false);
        source.RemoveAt(source.Count - 1);
        destination.Add(entry);
        return count;
    }

    internal static EditRegion Bounds(IEnumerable<Vector> positions)
    {
        using var iterator = positions.GetEnumerator();
        if (!iterator.MoveNext())
            throw new ArgumentException("The area is empty.");
        var min = iterator.Current;
        var max = min;
        while (iterator.MoveNext())
        {
            var p = iterator.Current;
            min = new Vector(Math.Min(min.X, p.X), Math.Min(min.Y, p.Y), Math.Min(min.Z, p.Z));
            max = new Vector(Math.Max(max.X, p.X), Math.Max(max.Y, p.Y), Math.Max(max.Z, p.Z));
        }
        return new EditRegion(min, max);
    }
}

internal sealed record BlockChange(Vector Position, BlockSnapshot Before, BlockSnapshot After);
internal sealed record EditHistory(ILevel Level, List<BlockChange> Changes);
