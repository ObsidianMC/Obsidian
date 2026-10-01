using Obsidian.Nbt;
using System.IO;
using Obsidian.WorldData.Features.Tree;

namespace Obsidian.WorldData.Structures;

/// <summary>
/// A structure template (<c>data/minecraft/structure/*.nbt</c>) and vanilla's <c>StructureTemplate.placeInWorld</c>,
/// ported as far as feature templates (fossils) need it.
/// </summary>
/// <remarks>
/// Not supported: mirroring, entities, block entity data (blocks with NBT are placed without it) and the waterlogging
/// pass for waterloggable template blocks. Templates are immutable after loading and safe to share between threads.
/// </remarks>
public sealed class StructureTemplate
{
    private readonly List<IReadOnlyList<StructureBlockInfo>> palettes;

    private StructureTemplate(Vector size, List<IReadOnlyList<StructureBlockInfo>> palettes)
    {
        this.Size = size;
        this.palettes = palettes;
    }

    /// <summary>Size of the template before rotation.</summary>
    public Vector Size { get; }

    /// <summary>Reads a template from vanilla's gzipped NBT format.</summary>
    public static StructureTemplate Load(Stream stream)
    {
        var reader = new NbtReader(stream, NbtCompression.GZip);
        var root = (NbtCompound)reader.ReadNextTag()!;

        var sizeList = (NbtList)root["size"];
        var size = new Vector(IntAt(sizeList, 0), IntAt(sizeList, 1), IntAt(sizeList, 2));
        var blocks = root.TryGetTag("blocks", out var blockTag) ? (NbtList)blockTag : null;

        var palettes = new List<IReadOnlyList<StructureBlockInfo>>();
        if (root.TryGetTag("palettes", out var paletteList))
        {
            foreach (var palette in (NbtList)paletteList)
                palettes.Add(LoadPalette((NbtList)palette, blocks));
        }
        else if (root.TryGetTag("palette", out var palette))
        {
            palettes.Add(LoadPalette((NbtList)palette, blocks));
        }

        return new StructureTemplate(size, palettes);
    }

    /// <summary>Vanilla <c>getSize(rotation)</c>: X and Z swap on quarter turns.</summary>
    public Vector GetSize(StructureRotation rotation) =>
        rotation is StructureRotation.Clockwise90 or StructureRotation.CounterClockwise90
            ? new Vector(this.Size.Z, this.Size.Y, this.Size.X)
            : this.Size;

    /// <summary>Vanilla <c>transform(pos, Mirror.NONE, rotation, pivot)</c>.</summary>
    public static Vector Transform(Vector position, StructureRotation rotation, Vector pivot) => rotation switch
    {
        StructureRotation.CounterClockwise90 => new Vector(pivot.X - pivot.Z + position.Z, position.Y, pivot.X + pivot.Z - position.X),
        StructureRotation.Clockwise90 => new Vector(pivot.X + pivot.Z - position.Z, position.Y, pivot.Z - pivot.X + position.X),
        StructureRotation.Clockwise180 => new Vector(pivot.X + pivot.X - position.X, position.Y, pivot.Z + pivot.Z - position.Z),
        _ => position
    };

    /// <summary>
    /// Vanilla <c>getZeroPositionWithTransform</c>: the origin to place at so the rotated template covers the same
    /// area as the unrotated one placed at <paramref name="position"/>.
    /// </summary>
    public Vector GetZeroPositionWithTransform(Vector position, StructureRotation rotation)
    {
        var maxX = this.Size.X - 1;
        var maxZ = this.Size.Z - 1;
        return rotation switch
        {
            StructureRotation.CounterClockwise90 => position + (0, 0, maxX),
            StructureRotation.Clockwise90 => position + (maxZ, 0, 0),
            StructureRotation.Clockwise180 => position + (maxX, 0, maxZ),
            _ => position
        };
    }

    /// <summary>Vanilla <c>getBoundingBox(settings, pos)</c>: the world box the template covers when placed at <paramref name="position"/>.</summary>
    public BlockBox GetBoundingBox(StructurePlaceSettings settings, Vector position)
    {
        var far = this.Size - 1;
        var a = Transform(Vector.Zero, settings.Rotation, settings.RotationPivot);
        var b = Transform(far, settings.Rotation, settings.RotationPivot);
        return BlockBox.FromCorners(a, b).Move(position);
    }

    /// <summary>
    /// Places the template with its origin at <paramref name="position"/>, running <see cref="StructurePlaceSettings.Processors"/>.
    /// Returns <c>false</c> when the template is empty.
    /// </summary>
    /// <param name="pivot">World position passed to processors as the pivot.</param>
    public bool PlaceInWorld(IWorldGenLevel level, Vector position, Vector pivot, StructurePlaceSettings settings)
    {
        if (this.palettes.Count == 0)
            return false;

        var blocks = settings.GetRandomPalette(this.palettes, position);
        if (blocks.Count == 0 || this.Size.X < 1 || this.Size.Y < 1 || this.Size.Z < 1)
            return false;

        var box = settings.BoundingBox;
        var placed = new List<Vector>(blocks.Count);
        var min = new Vector(int.MaxValue, int.MaxValue, int.MaxValue);
        var max = new Vector(int.MinValue, int.MinValue, int.MinValue);

        foreach (var info in ProcessBlockInfos(level, position, pivot, settings, blocks))
        {
            var target = info.Position;
            if (box is not null && !box.Value.IsInside(target))
                continue;

            if (!level.SetBlock(target, settings.Rotation.Rotate(info.Block)))
                continue;

            min = new Vector(Math.Min(min.X, target.X), Math.Min(min.Y, target.Y), Math.Min(min.Z, target.Z));
            max = new Vector(Math.Max(max.X, target.X), Math.Max(max.Y, target.Y), Math.Max(max.Z, target.Z));
            placed.Add(target);
        }

        if (placed.Count > 0)
        {
            // Neighbor shape updates around the placed blocks (vanilla skips them only for "known shape" placements).
            var shape = new TreeVoxelShape(max.X - min.X + 1, max.Y - min.Y + 1, max.Z - min.Z + 1);
            foreach (var target in placed)
                shape.Fill(target.X - min.X, target.Y - min.Y, target.Z - min.Z);

            ShapeUpdater.UpdateShapeAtEdge(level, shape, min);
        }

        return true;
    }

    /// <summary>
    /// Vanilla <c>processBlockInfos</c>: moves each block to its world position and runs the processors in order,
    /// dropping blocks a processor rejects.
    /// </summary>
    private static List<StructureBlockInfo> ProcessBlockInfos(IWorldGenLevel level, Vector origin, Vector pivot, StructurePlaceSettings settings,
        IReadOnlyList<StructureBlockInfo> blocks)
    {
        var result = new List<StructureBlockInfo>(blocks.Count);
        foreach (var original in blocks)
        {
            var target = Transform(original.Position, settings.Rotation, settings.RotationPivot) + origin;
            StructureBlockInfo? current = original with { Position = target };

            foreach (var processor in settings.Processors)
            {
                current = processor.ProcessBlock(level, origin, pivot, original, current.Value, settings);
                if (current is null)
                    break;
            }

            if (current is not null)
                result.Add(current.Value);
        }

        return result;
    }

    /// <summary>
    /// Vanilla <c>loadPalette</c>: full collision cubes first, then other blocks, then blocks with block entity data,
    /// each sorted by Y, X, Z. Processors draw random values in this order.
    /// </summary>
    private static List<StructureBlockInfo> LoadPalette(NbtList palette, NbtList? blocks)
    {
        var states = new List<IBlock>(palette.Count);
        foreach (NbtCompound entry in palette)
        {
            var properties = entry.TryGetTag("Properties", out var tag)
                ? ((NbtCompound)tag).ToDictionary(property => property.Key, property => ((NbtTag<string>)property.Value).Value)
                : null;
            states.Add(BlockStateProperties.GetState(entry.GetString("Name"), properties));
        }

        var fullBlocks = new List<StructureBlockInfo>();
        var withNbt = new List<StructureBlockInfo>();
        var others = new List<StructureBlockInfo>();
        foreach (NbtCompound entry in (IEnumerable<INbtTag>?)blocks ?? [])
        {
            var pos = (NbtList)entry["pos"];
            var block = states[entry.GetInt("state")];
            var nbt = entry.TryGetTag("nbt", out var nbtTag) ? (NbtCompound)nbtTag : null;
            var info = new StructureBlockInfo(new Vector(IntAt(pos, 0), IntAt(pos, 1), IntAt(pos, 2)), block, nbt);

            if (nbt is not null)
                withNbt.Add(info);
            else if (!HasDynamicShape(block) && block.IsCollisionShapeFullBlock())
                fullBlocks.Add(info);
            else
                others.Add(info);
        }

        Comparison<StructureBlockInfo> order = (a, b) =>
        {
            var compared = a.Position.Y.CompareTo(b.Position.Y);
            if (compared == 0)
                compared = a.Position.X.CompareTo(b.Position.X);

            return compared != 0 ? compared : a.Position.Z.CompareTo(b.Position.Z);
        };

        fullBlocks.Sort(order);
        others.Sort(order);
        withNbt.Sort(order);
        return [.. fullBlocks, .. others, .. withNbt];
    }

    /// <summary>Blocks vanilla flags with <c>dynamicShape()</c> (shapes that depend on block entity state).</summary>
    private static bool HasDynamicShape(IBlock block) => block.BlockClass() is "ShulkerBoxBlock" or "MovingPistonBlock";

    private static int IntAt(NbtList list, int index) => ((NbtTag<int>)list[index]).Value;
}
