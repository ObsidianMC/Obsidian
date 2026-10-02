using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Nbt;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Features.Tree;
using System.IO;

namespace Obsidian.WorldData.Structures;

/// <summary>
/// A structure template (<c>data/minecraft/structure/*.nbt</c>) and vanilla's <c>StructureTemplate</c> placement logic.
/// </summary>
/// <remarks>Templates are immutable after loading and safe to share between threads.</remarks>
public sealed class StructureTemplate
{
    private static readonly IBlock barrier = BlocksRegistry.Get(Material.Barrier);

    // Vanilla's waterlogging pass looks for water sources in these directions.
    private static readonly Vector[] waterSourceDirections = [Vector.Up, new(0, 0, -1), new(1, 0, 0), new(0, 0, 1), new(-1, 0, 0)];

    // Block entities implementing vanilla's RandomizableContainer: placement gives them a loot table seed.
    private static readonly HashSet<string> randomizableContainers =
    [
        "minecraft:barrel", "minecraft:chest", "minecraft:crafter", "minecraft:decorated_pot", "minecraft:dispenser",
        "minecraft:dropper", "minecraft:hopper", "minecraft:shulker_box", "minecraft:trapped_chest"
    ];

    // Blocks with vanilla's hasPostProcess property.
    private static readonly BlockSet postProcessBlocks = new("minecraft:brown_mushroom", "minecraft:red_mushroom", "minecraft:magma_block");

    private readonly List<Palette> palettes;
    private readonly List<StructureEntityInfo> entities;

    private StructureTemplate(Vector size, List<Palette> palettes, List<StructureEntityInfo> entities)
    {
        this.Size = size;
        this.palettes = palettes;
        this.entities = entities;
    }

    /// <summary>Size of the template before rotation.</summary>
    public Vector Size { get; }

    /// <summary>A template without size or blocks, like vanilla's <c>new StructureTemplate()</c>.</summary>
    public static StructureTemplate CreateEmpty() => new(Vector.Zero, [], []);

    /// <summary>Reads a template from vanilla's NBT format, gzipped unless <paramref name="compression"/> says otherwise.</summary>
    public static StructureTemplate Load(Stream stream, NbtCompression compression = NbtCompression.GZip) =>
        // Most of a template is its block list, and building tags for all of it is slow: uncompressed data that can be
        // seeked is read field by field instead.
        compression == NbtCompression.None && stream.CanSeek ? LoadFields(stream) : LoadTags(stream, compression);

    private static StructureTemplate LoadTags(Stream stream, NbtCompression compression)
    {
        var reader = new NbtReader(stream, compression);
        var root = (NbtCompound)reader.ReadNextTag()!;

        var size = root.TryGetTag<NbtList>("size", out var sizeList) ? ReadVector(sizeList) : Vector.Zero;
        var blocks = root.TryGetTag<NbtList>("blocks", out var blockList) ? blockList : null;

        var templateBlocks = new List<TemplateBlock>();
        foreach (NbtCompound entry in (IEnumerable<INbtTag>?)blocks ?? [])
        {
            var state = entry.TryGetTag<NbtTag<int>>("state", out var stateTag) ? stateTag.Value : 0;
            var nbt = entry.TryGetTag<NbtCompound>("nbt", out var nbtTag) ? nbtTag : null;
            templateBlocks.Add(new TemplateBlock(ReadVector((NbtList)entry["pos"]), state, nbt));
        }

        var palettes = new List<Palette>();
        if (root.TryGetTag<NbtList>("palettes", out var paletteList))
        {
            foreach (var palette in paletteList)
                palettes.Add(BuildPalette(ReadPaletteStates((NbtList)palette), templateBlocks));
        }
        else if (root.TryGetTag<NbtList>("palette", out var palette))
        {
            palettes.Add(BuildPalette(ReadPaletteStates(palette), templateBlocks));
        }

        var entities = new List<StructureEntityInfo>();
        if (root.TryGetTag<NbtList>("entities", out var entityList))
        {
            foreach (NbtCompound entity in entityList)
            {
                if (!entity.TryGetTag<NbtCompound>("nbt", out var nbt))
                    continue;

                var position = entity.TryGetTag<NbtList>("pos", out var pos)
                    ? new EntityPosition(DoubleAt(pos, 0), DoubleAt(pos, 1), DoubleAt(pos, 2))
                    : default;
                var blockPosition = entity.TryGetTag<NbtList>("blockPos", out var blockPos) ? ReadVector(blockPos) : Vector.Zero;
                entities.Add(new StructureEntityInfo(position, blockPosition, NbtCopy.Copy(nbt)));
            }
        }

        return new StructureTemplate(size, palettes, entities);
    }

    /// <summary>Vanilla <c>getSize(rotation)</c>: X and Z swap on quarter turns.</summary>
    public Vector GetSize(StructureRotation rotation) =>
        rotation is StructureRotation.Clockwise90 or StructureRotation.CounterClockwise90
            ? new Vector(this.Size.Z, this.Size.Y, this.Size.X)
            : this.Size;

    /// <summary>Vanilla <c>transform(pos, mirror, rotation, pivot)</c> for block positions.</summary>
    public static Vector Transform(Vector position, StructureMirror mirror, StructureRotation rotation, Vector pivot)
    {
        var x = position.X;
        var z = position.Z;
        switch (mirror)
        {
            case StructureMirror.LeftRight:
                z = -z;
                break;
            case StructureMirror.FrontBack:
                x = -x;
                break;
        }

        return rotation switch
        {
            StructureRotation.CounterClockwise90 => new Vector(pivot.X - pivot.Z + z, position.Y, pivot.X + pivot.Z - x),
            StructureRotation.Clockwise90 => new Vector(pivot.X + pivot.Z - z, position.Y, pivot.Z - pivot.X + x),
            StructureRotation.Clockwise180 => new Vector(pivot.X + pivot.X - x, position.Y, pivot.Z + pivot.Z - z),
            _ => new Vector(x, position.Y, z)
        };
    }

    /// <summary>Vanilla <c>transform(pos, Mirror.NONE, rotation, pivot)</c>.</summary>
    public static Vector Transform(Vector position, StructureRotation rotation, Vector pivot) =>
        Transform(position, StructureMirror.None, rotation, pivot);

    /// <summary>Vanilla <c>calculateRelativePosition</c>: a template position transformed by the settings.</summary>
    public static Vector CalculateRelativePosition(StructurePlaceSettings settings, Vector position) =>
        Transform(position, settings.Mirror, settings.Rotation, settings.RotationPivot);

    /// <summary>
    /// Vanilla <c>getZeroPositionWithTransform</c>: the origin to place at so the transformed template covers the same
    /// area as the untransformed one placed at <paramref name="position"/>.
    /// </summary>
    public Vector GetZeroPositionWithTransform(Vector position, StructureMirror mirror, StructureRotation rotation) =>
        GetZeroPositionWithTransform(position, mirror, rotation, this.Size.X, this.Size.Z);

    public Vector GetZeroPositionWithTransform(Vector position, StructureRotation rotation) =>
        this.GetZeroPositionWithTransform(position, StructureMirror.None, rotation);

    /// <summary>Vanilla's static <c>getZeroPositionWithTransform</c> for a template of the given width and depth.</summary>
    public static Vector GetZeroPositionWithTransform(Vector position, StructureMirror mirror, StructureRotation rotation, int sizeX, int sizeZ)
    {
        var maxX = sizeX - 1;
        var maxZ = sizeZ - 1;
        var mirrorX = mirror == StructureMirror.FrontBack ? maxX : 0;
        var mirrorZ = mirror == StructureMirror.LeftRight ? maxZ : 0;
        return rotation switch
        {
            StructureRotation.CounterClockwise90 => position + (mirrorZ, 0, maxX - mirrorX),
            StructureRotation.Clockwise90 => position + (maxZ - mirrorZ, 0, mirrorX),
            StructureRotation.Clockwise180 => position + (maxX - mirrorX, 0, maxZ - mirrorZ),
            _ => position + (mirrorX, 0, mirrorZ)
        };
    }

    /// <summary>Vanilla <c>getBoundingBox(settings, pos)</c>: the world box the template covers when placed at <paramref name="position"/>.</summary>
    public BlockBox GetBoundingBox(StructurePlaceSettings settings, Vector position) =>
        this.GetBoundingBox(position, settings.Rotation, settings.RotationPivot, settings.Mirror);

    /// <summary>
    /// The template's box at <paramref name="position"/> when rotated around its origin without mirroring, like
    /// <see cref="GetBoundingBox(StructurePlaceSettings, Vector)"/> with default settings besides the rotation.
    /// </summary>
    public BlockBox GetBoundingBox(Vector position, StructureRotation rotation) => this.RotatedBoxes[(int)rotation].Move(position);

    // The box of each rotation at the origin: jigsaw assembly asks for them over and over.
    private BlockBox[] RotatedBoxes => field ??=
        [.. Enum.GetValues<StructureRotation>().Select(rotation => this.GetBoundingBox(Vector.Zero, rotation, Vector.Zero, StructureMirror.None))];

    public BlockBox GetBoundingBox(Vector position, StructureRotation rotation, Vector pivot, StructureMirror mirror)
    {
        var a = Transform(Vector.Zero, mirror, rotation, pivot);
        var b = Transform(this.Size - 1, mirror, rotation, pivot);
        return BlockBox.FromCorners(a, b).Move(position);
    }

    /// <summary>
    /// Vanilla <c>filterBlocks</c>: the blocks of type <paramref name="block"/> in a random palette, with their state
    /// rotated, at world positions (or template positions when <paramref name="relative"/> is false), inside the settings' box.
    /// </summary>
    public List<StructureBlockInfo> FilterBlocks(Vector position, StructurePlaceSettings settings, Material block, bool relative = true)
    {
        var result = new List<StructureBlockInfo>();
        if (this.palettes.Count == 0)
            return result;

        var box = settings.BoundingBox;
        foreach (var info in settings.GetRandomPalette(this.palettes, position).BlocksOf(block))
        {
            var target = relative ? CalculateRelativePosition(settings, info.Position) + position : info.Position;
            if (box is null || box.Value.IsInside(target))
                result.Add(new StructureBlockInfo(target, info.Block.Rotate(settings.Rotation), info.Nbt));
        }

        return result;
    }

    /// <summary>
    /// Vanilla <c>getJigsaws</c>: the jigsaw blocks of a random palette at world positions, rotated.
    /// </summary>
    public void AddJigsaws(List<JigsawBlockInfo> destination, Vector position, StructureRotation rotation)
    {
        if (this.palettes.Count == 0)
            return;

        // A single palette is always picked, so the position-seeded pick can be skipped.
        var palette = this.palettes.Count == 1 ? this.palettes[0] : new StructurePlaceSettings().GetRandomPalette(this.palettes, position);
        var rotated = palette.RotatedJigsaws(rotation);
        if (position == Vector.Zero)
        {
            destination.AddRange(rotated);
            return;
        }

        foreach (var jigsaw in rotated)
            destination.Add(jigsaw with { Info = jigsaw.Info with { Position = jigsaw.Info.Position + position } });
    }

    /// <summary>
    /// Vanilla <c>placeInWorld</c>: places the template with its origin at <paramref name="position"/>, running the
    /// settings' processors, filling block entities, waterlogging, updating shapes and adding entities. Returns <c>false</c>
    /// when there's nothing to place.
    /// </summary>
    /// <param name="pivot">World position passed to processors as the pivot.</param>
    /// <param name="random">Draws the loot table seeds of containers.</param>
    public bool PlaceInWorld(IWorldGenLevel level, Vector position, Vector pivot, StructurePlaceSettings settings, IRandomSource random)
    {
        if (this.palettes.Count == 0)
            return false;

        var blocks = settings.GetRandomPalette(this.palettes, position).Blocks;
        if (blocks.Count == 0 && (settings.IgnoreEntities || this.entities.Count == 0) || this.Size.X < 1 || this.Size.Y < 1 || this.Size.Z < 1)
            return false;

        var box = settings.BoundingBox;
        var applyWaterlogging = settings.ShouldApplyWaterlogging;
        var toWaterlog = new List<Vector>();
        var placedSources = new HashSet<Vector>();
        var min = new Vector(int.MaxValue, int.MaxValue, int.MaxValue);
        var max = new Vector(int.MinValue, int.MinValue, int.MinValue);

        // Vanilla processes every block of the template, then drops those outside the box. When every processor works block
        // by block with randoms seeded from positions, skipping the blocks outside the box's columns first gives the same
        // result for much less work, since a piece is placed once for every chunk it reaches.
        var clip = box is not null && settings.Random is null && !settings.Processors.Any(processor => processor.ProcessesWholeTemplate) ? box : null;
        var infos = ProcessBlockInfos(level, position, pivot, settings, blocks, clip);
        var placed = new List<(Vector Position, NbtCompound? Nbt)>(infos.Count);

        foreach (var info in infos)
        {
            var target = info.Position;
            if (box is not null && !box.Value.IsInside(target))
                continue;

            var existing = applyWaterlogging ? level.GetBlock(target) : null;
            var state = info.Block.Mirror(settings.Mirror).Rotate(settings.Rotation);

            // Vanilla clears the position first so the block entity is created fresh.
            if (info.Nbt is not null)
                level.SetBlock(target, barrier);

            if (!level.SetBlock(target, state))
                continue;

            min = new Vector(Math.Min(min.X, target.X), Math.Min(min.Y, target.Y), Math.Min(min.Z, target.Z));
            max = new Vector(Math.Max(max.X, target.X), Math.Max(max.Y, target.Y), Math.Max(max.Z, target.Z));
            placed.Add((target, info.Nbt));

            // Vanilla's WorldGenRegion marks these unless the placement flags say the shape is known.
            if (!settings.KnownShape && postProcessBlocks.Contains(state))
                level.MarkForPostProcessing(target);

            if (info.Nbt is not null && level.GetBlockEntity(target) is DataBlockEntity blockEntity)
                LoadBlockEntity(blockEntity, info.Nbt, random);

            if (existing is not null)
            {
                if (state.IsFluidSource())
                {
                    placedSources.Add(target);
                }
                else if (IsLiquidBlockContainer(state))
                {
                    PlaceLiquid(level, target, state, existing);
                    if (!existing.IsFluidSource())
                        toWaterlog.Add(target);
                }
            }
        }

        WaterlogFromNeighbors(level, toWaterlog, placedSources);

        if (min.X <= max.X)
        {
            if (!settings.KnownShape)
            {
                var shape = new TreeVoxelShape(max.X - min.X + 1, max.Y - min.Y + 1, max.Z - min.Z + 1);
                foreach (var (target, _) in placed)
                    shape.Fill(target.X - min.X, target.Y - min.Y, target.Z - min.Z);

                ShapeUpdater.UpdateShapeAtEdge(level, shape, min);

                foreach (var (target, _) in placed)
                {
                    var current = level.GetBlock(target);
                    var updated = ShapeUpdater.UpdateFromNeighbourShapes(level, current, target);
                    if (!updated.IsSameState(current))
                        level.SetBlock(target, updated);
                }
            }
        }

        if (!settings.IgnoreEntities)
            this.PlaceEntities(level, position, settings, box);

        return true;
    }

    /// <summary>
    /// Vanilla <c>processBlockInfos</c>: moves each block to its world position and runs the processors in order,
    /// dropping blocks a processor rejects, then lets each processor finalize the whole list.
    /// </summary>
    /// <param name="clip">Skips blocks whose world column is outside this box; only for processors that don't depend on the
    /// other blocks (processors may move blocks vertically, never horizontally).</param>
    public static List<StructureBlockInfo> ProcessBlockInfos(IWorldGenLevel level, Vector origin, Vector pivot, StructurePlaceSettings settings,
        IReadOnlyList<StructureBlockInfo> blocks, BlockBox? clip = null)
    {
        // A clipped template keeps the blocks of a few columns, often a small part of it.
        var capacity = clip is null ? blocks.Count : 0;
        var originals = new List<StructureBlockInfo>(capacity);
        var result = new List<StructureBlockInfo>(capacity);
        foreach (var original in blocks)
        {
            var target = CalculateRelativePosition(settings, original.Position) + origin;
            if (clip is not null && !clip.Value.Intersects(target.X, target.Z, target.X, target.Z))
                continue;

            StructureBlockInfo? current = original with { Position = target };

            foreach (var processor in settings.Processors)
            {
                current = processor.ProcessBlock(level, origin, pivot, original, current.Value, settings);
                if (current is null)
                    break;
            }

            if (current is not null)
            {
                result.Add(current.Value);
                originals.Add(original);
            }
        }

        foreach (var processor in settings.Processors)
            result = processor.FinalizeProcessing(level, origin, pivot, originals, result, settings);

        return result;
    }

    /// <summary>
    /// Fills a placed block entity from template data like vanilla's <c>loadWithComponents</c>, keeping what vanilla would
    /// save. Containers get a loot table seed from <paramref name="random"/>.
    /// </summary>
    private static void LoadBlockEntity(DataBlockEntity blockEntity, NbtCompound nbt, IRandomSource random)
    {
        var data = NbtCopy.Copy(nbt, "id", "x", "y", "z");
        if (randomizableContainers.Contains(blockEntity.Id))
        {
            var seed = random.NextLong();
            data.Remove("LootTableSeed");
            if (data.HasTag("LootTable"))
            {
                // A container with a loot table keeps no items until it's opened, and a zero seed isn't saved.
                data.Remove("Items");
                if (seed != 0L)
                    data.Add("LootTableSeed", new NbtTag<long>("LootTableSeed", seed));
            }
        }

        blockEntity.Data.Clear();
        foreach (var (_, tag) in data.ToList())
            blockEntity.Set(tag);
    }

    /// <summary>
    /// Blocks implementing vanilla's <c>LiquidBlockContainer</c>: waterloggable blocks, kelp and seagrass.
    /// </summary>
    private static bool IsLiquidBlockContainer(IBlock block) =>
        block.HasProperty("waterlogged") || block.Material is Material.Kelp or Material.KelpPlant or Material.Seagrass or Material.TallSeagrass;

    /// <summary>
    /// Vanilla <c>SimpleWaterloggedBlock.placeLiquid</c>: a dry waterloggable block takes a water source's water.
    /// </summary>
    private static void PlaceLiquid(IWorldGenLevel level, Vector position, IBlock state, IBlock fluidSource)
    {
        if (state.GetProperty("waterlogged") != "false" || fluidSource.GetFluid() != FluidKind.Water)
            return;

        level.SetBlock(position, state.WithProperty("waterlogged", true));
        level.ScheduleFluidTick(position);
    }

    /// <summary>
    /// Vanilla's second waterlogging pass: blocks next to a water source the template didn't place take its water, repeated
    /// while anything changes.
    /// </summary>
    private static void WaterlogFromNeighbors(IWorldGenLevel level, List<Vector> positions, HashSet<Vector> placedSources)
    {
        var changed = true;
        while (changed && positions.Count > 0)
        {
            changed = false;
            for (var index = 0; index < positions.Count; index++)
            {
                var position = positions[index];
                var fluid = level.GetBlock(position);

                for (var direction = 0; direction < waterSourceDirections.Length && !fluid.IsFluidSource(); direction++)
                {
                    var neighborPosition = position + waterSourceDirections[direction];
                    var neighbor = level.GetBlock(neighborPosition);
                    if (neighbor.IsFluidSource() && !placedSources.Contains(neighborPosition))
                        fluid = neighbor;
                }

                if (!fluid.IsFluidSource())
                    continue;

                var state = level.GetBlock(position);
                if (IsLiquidBlockContainer(state))
                {
                    PlaceLiquid(level, position, state, fluid);
                    changed = true;
                    positions.RemoveAt(index--);
                }
            }
        }
    }

    /// <summary>
    /// Vanilla <c>placeEntities</c>: adds the template's entities, transformed with the template.
    /// </summary>
    private void PlaceEntities(IWorldGenLevel level, Vector origin, StructurePlaceSettings settings, BlockBox? box)
    {
        foreach (var entity in this.entities)
        {
            var blockPosition = Transform(entity.BlockPosition, settings.Mirror, settings.Rotation, settings.RotationPivot) + origin;
            if (box is not null && !box.Value.IsInside(blockPosition) || !entity.Nbt.TryGetTag<NbtTag<string>>("id", out var id))
                continue;

            var position = TransformEntityPosition(entity.Position, settings.Mirror, settings.Rotation, settings.RotationPivot);
            var yaw = 0f;
            var pitch = 0f;
            if (entity.Nbt.TryGetTag<NbtList>("Rotation", out var rotation) && rotation.Count == 2)
            {
                yaw = ((NbtTag<float>)rotation[0]).Value;
                pitch = ((NbtTag<float>)rotation[1]).Value;
            }

            // Vanilla keeps the rotation within a turn when the entity is loaded into the level (Entity.setRot).
            level.AddEntity(new GeneratedEntity(id.Value!,
                new VectorF((float)(position.X + origin.X), (float)(position.Y + origin.Y), (float)(position.Z + origin.Z)),
                RotateYaw(yaw, settings.Mirror, settings.Rotation) % 360f, pitch % 360f)
            {
                Data = NbtCopy.Copy(entity.Nbt, "id", "Pos", "Rotation", "UUID")
            });
        }
    }

    /// <summary>
    /// Vanilla's entity yaw after <c>Entity.rotate</c> and <c>Entity.mirror</c>.
    /// </summary>
    private static float RotateYaw(float yaw, StructureMirror mirror, StructureRotation rotation)
    {
        var wrapped = WrapDegrees(yaw);
        var rotated = rotation switch
        {
            StructureRotation.Clockwise180 => wrapped + 180f,
            StructureRotation.CounterClockwise90 => wrapped + 270f,
            StructureRotation.Clockwise90 => wrapped + 90f,
            _ => wrapped
        };
        var mirrored = mirror switch
        {
            StructureMirror.FrontBack => -wrapped,
            StructureMirror.LeftRight => 180f - wrapped,
            _ => wrapped
        };

        return rotated + mirrored - yaw;
    }

    /// <summary>Vanilla <c>Mth.wrapDegrees(float)</c>.</summary>
    private static float WrapDegrees(float degrees)
    {
        var wrapped = degrees % 360f;
        if (wrapped >= 180f)
            wrapped -= 360f;

        if (wrapped < -180f)
            wrapped += 360f;

        return wrapped;
    }

    /// <summary>Vanilla <c>transform(Vec3, mirror, rotation, pivot)</c>: like block positions, but around block centers.</summary>
    private static EntityPosition TransformEntityPosition(EntityPosition position, StructureMirror mirror, StructureRotation rotation, Vector pivot)
    {
        var x = position.X;
        var z = position.Z;
        switch (mirror)
        {
            case StructureMirror.LeftRight:
                z = 1.0 - z;
                break;
            case StructureMirror.FrontBack:
                x = 1.0 - x;
                break;
        }

        return rotation switch
        {
            StructureRotation.CounterClockwise90 => new EntityPosition(pivot.X - pivot.Z + z, position.Y, pivot.X + pivot.Z + 1 - x),
            StructureRotation.Clockwise90 => new EntityPosition(pivot.X + pivot.Z + 1 - z, position.Y, pivot.Z - pivot.X + x),
            StructureRotation.Clockwise180 => new EntityPosition(pivot.X + pivot.X + 1 - x, position.Y, pivot.Z + pivot.Z + 1 - z),
            _ => new EntityPosition(x, position.Y, z)
        };
    }

    /// <summary>
    /// Vanilla <c>loadPalette</c>: full collision cubes first, then other blocks, then blocks with block entity data,
    /// each sorted by Y, X, Z. Processors draw random values in this order.
    /// </summary>
    private static List<IBlock> ReadPaletteStates(NbtList palette)
    {
        var states = new List<IBlock>(palette.Count);
        foreach (NbtCompound entry in palette)
        {
            var properties = entry.TryGetTag<NbtCompound>("Properties", out var tag)
                ? tag.ToDictionary(property => property.Key, property => ((NbtTag<string>)property.Value).Value!)
                : null;
            states.Add(BlockStateProperties.GetState(entry.GetString("Name")!, properties));
        }

        return states;
    }

    private static Palette BuildPalette(List<IBlock> states, List<TemplateBlock> blocks)
    {
        // Whether each state is a full cube without a dynamic shape, checked once rather than for every block.
        var fullCubes = states.Select(state => !HasDynamicShape(state) && state.IsCollisionShapeFullBlock()).ToArray();

        var fullBlocks = new List<StructureBlockInfo>();
        var withNbt = new List<StructureBlockInfo>();
        var others = new List<StructureBlockInfo>();
        foreach (var (position, state, blockNbt) in blocks)
        {
            var known = state < states.Count;
            var block = known ? states[state] : BlocksRegistry.Air;
            var nbt = blockNbt is not null ? NbtCopy.Copy(blockNbt) : null;
            var info = new StructureBlockInfo(position, block, nbt);

            // Air, used for unknown states, isn't a full cube.
            if (nbt is not null)
                withNbt.Add(info);
            else if (known && fullCubes[state])
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

        // List.Sort isn't stable, but positions are unique within a palette.
        fullBlocks.Sort(order);
        others.Sort(order);
        withNbt.Sort(order);
        return new Palette([.. fullBlocks, .. others, .. withNbt]);
    }

    /// <summary>
    /// <see cref="LoadTags"/> without building tags for the whole template: the fields are read as they come, and only
    /// block and entity data become tags.
    /// </summary>
    /// <remarks>Field names are compared as bytes; only values that are kept are read as strings.</remarks>
    private static StructureTemplate LoadFields(Stream stream)
    {
        var reader = new NbtReader(stream);
        if ((NbtTagType)reader.ReadByte() != NbtTagType.Compound)
            throw new InvalidDataException("A structure template's root isn't a compound.");

        Span<byte> nameBuffer = stackalloc byte[MaxFieldName];
        reader.ReadString();
        var size = Vector.Zero;
        var blocks = new List<TemplateBlock>();
        List<List<IBlock>>? paletteStates = null;
        List<IBlock>? singlePaletteStates = null;
        var entities = new List<StructureEntityInfo>();

        while (true)
        {
            var type = (NbtTagType)reader.ReadByte();
            if (type == NbtTagType.End)
                break;

            var name = ReadFieldName(reader, nameBuffer);
            if (type != NbtTagType.List)
            {
                SkipPayload(reader, type);
            }
            else if (name.SequenceEqual("size"u8))
            {
                size = ReadVector(reader);
            }
            else if (name.SequenceEqual("blocks"u8))
            {
                var (elementType, count) = ReadListHeader(reader);
                for (var i = 0; i < count; i++)
                {
                    if (elementType == NbtTagType.Compound)
                        blocks.Add(ReadBlock(reader));
                    else
                        SkipPayload(reader, elementType);
                }
            }
            else if (name.SequenceEqual("palette"u8))
            {
                singlePaletteStates = ReadPaletteStates(reader);
            }
            else if (name.SequenceEqual("palettes"u8))
            {
                paletteStates = [];
                var (elementType, count) = ReadListHeader(reader);
                for (var i = 0; i < count; i++)
                {
                    if (elementType == NbtTagType.List)
                        paletteStates.Add(ReadPaletteStates(reader));
                    else
                        SkipPayload(reader, elementType);
                }
            }
            else if (name.SequenceEqual("entities"u8))
            {
                var (elementType, count) = ReadListHeader(reader);
                for (var i = 0; i < count; i++)
                {
                    if (elementType != NbtTagType.Compound)
                    {
                        SkipPayload(reader, elementType);
                        continue;
                    }

                    var entity = ReadEntity(reader);
                    if (entity is not null)
                        entities.Add(entity);
                }
            }
            else
            {
                SkipPayload(reader, type);
            }
        }

        var palettes = new List<Palette>();
        if (paletteStates is not null)
        {
            foreach (var states in paletteStates)
                palettes.Add(BuildPalette(states, blocks));
        }
        else if (singlePaletteStates is not null)
        {
            palettes.Add(BuildPalette(singlePaletteStates, blocks));
        }

        return new StructureTemplate(size, palettes, entities);
    }

    // The longest field name compared; longer names match none of the fields read.
    private const int MaxFieldName = 32;

    // Reads a field name's bytes into the buffer; a longer name is skipped and gives an empty span.
    private static ReadOnlySpan<byte> ReadFieldName(NbtReader reader, Span<byte> buffer)
    {
        int length = reader.ReadInt16();
        if (length <= 0)
            return [];

        if (length > buffer.Length)
        {
            reader.BaseStream.Seek(length, SeekOrigin.Current);
            return [];
        }

        var name = buffer[..length];
        reader.BaseStream.ReadExactly(name);
        return name;
    }

    private static (NbtTagType ElementType, int Count) ReadListHeader(NbtReader reader)
    {
        var type = (NbtTagType)reader.ReadByte();
        return (type, reader.ReadInt32());
    }

    // The fields of one compound element of "blocks".
    private static TemplateBlock ReadBlock(NbtReader reader)
    {
        Span<byte> nameBuffer = stackalloc byte[MaxFieldName];
        var position = Vector.Zero;
        var state = 0;
        NbtCompound? nbt = null;
        while (true)
        {
            var start = reader.BaseStream.Position;
            var type = (NbtTagType)reader.ReadByte();
            if (type == NbtTagType.End)
                break;

            var name = ReadFieldName(reader, nameBuffer);
            if (type == NbtTagType.List && name.SequenceEqual("pos"u8))
                position = ReadVector(reader);
            else if (type == NbtTagType.Int && name.SequenceEqual("state"u8))
                state = reader.ReadInt32();
            else if (type == NbtTagType.Compound && name.SequenceEqual("nbt"u8))
                nbt = ReadTagAt(reader, start);
            else
                SkipPayload(reader, type);
        }

        return new TemplateBlock(position, state, nbt);
    }

    private static List<IBlock> ReadPaletteStates(NbtReader reader)
    {
        Span<byte> nameBuffer = stackalloc byte[MaxFieldName];
        var (elementType, count) = ReadListHeader(reader);
        var states = new List<IBlock>(Math.Max(count, 0));
        for (var i = 0; i < count; i++)
        {
            if (elementType != NbtTagType.Compound)
            {
                SkipPayload(reader, elementType);
                continue;
            }

            string? blockName = null;
            Dictionary<string, string>? properties = null;
            while (true)
            {
                var type = (NbtTagType)reader.ReadByte();
                if (type == NbtTagType.End)
                    break;

                var name = ReadFieldName(reader, nameBuffer);
                if (type == NbtTagType.String && name.SequenceEqual("Name"u8))
                    blockName = reader.ReadString();
                else if (type == NbtTagType.Compound && name.SequenceEqual("Properties"u8))
                    properties = ReadProperties(reader);
                else
                    SkipPayload(reader, type);
            }

            states.Add(BlockStateProperties.GetState(blockName!, properties));
        }

        return states;
    }

    private static Dictionary<string, string> ReadProperties(NbtReader reader)
    {
        var properties = new Dictionary<string, string>();
        while (true)
        {
            var type = (NbtTagType)reader.ReadByte();
            if (type == NbtTagType.End)
                return properties;

            var property = reader.ReadString();
            if (type != NbtTagType.String)
                throw new InvalidDataException($"Block property '{property}' isn't a string.");

            properties.Add(property, reader.ReadString());
        }
    }

    private static StructureEntityInfo? ReadEntity(NbtReader reader)
    {
        Span<byte> nameBuffer = stackalloc byte[MaxFieldName];
        var position = default(EntityPosition);
        var blockPosition = Vector.Zero;
        NbtCompound? nbt = null;
        while (true)
        {
            var start = reader.BaseStream.Position;
            var type = (NbtTagType)reader.ReadByte();
            if (type == NbtTagType.End)
                break;

            var name = ReadFieldName(reader, nameBuffer);
            if (type == NbtTagType.List && name.SequenceEqual("pos"u8))
                position = ReadEntityPosition(reader);
            else if (type == NbtTagType.List && name.SequenceEqual("blockPos"u8))
                blockPosition = ReadVector(reader);
            else if (type == NbtTagType.Compound && name.SequenceEqual("nbt"u8))
                nbt = ReadTagAt(reader, start);
            else
                SkipPayload(reader, type);
        }

        return nbt is null ? null : new StructureEntityInfo(position, blockPosition, NbtCopy.Copy(nbt));
    }

    // Reads the compound field that starts at the position as a whole tag, like the tag tree would hold it.
    private static NbtCompound ReadTagAt(NbtReader reader, long start)
    {
        reader.BaseStream.Position = start;
        return (NbtCompound)reader.ReadNextTag()!;
    }

    // A list of ints like ReadVector(NbtList): the first three, zero when missing.
    private static Vector ReadVector(NbtReader reader)
    {
        Span<int> values = stackalloc int[3];
        values.Clear();
        var (type, count) = ReadListHeader(reader);
        for (var i = 0; i < count; i++)
        {
            if (type != NbtTagType.Int)
                throw new InvalidDataException("A template position isn't a list of ints.");

            var value = reader.ReadInt32();
            if (i < 3)
                values[i] = value;
        }

        return new Vector(values[0], values[1], values[2]);
    }

    // A list of doubles like DoubleAt: the first three, zero when missing.
    private static EntityPosition ReadEntityPosition(NbtReader reader)
    {
        Span<double> values = stackalloc double[3];
        values.Clear();
        var (type, count) = ReadListHeader(reader);
        for (var i = 0; i < count; i++)
        {
            if (type != NbtTagType.Double)
                throw new InvalidDataException("An entity position isn't a list of doubles.");

            var value = reader.ReadDouble();
            if (i < 3)
                values[i] = value;
        }

        return new EntityPosition(values[0], values[1], values[2]);
    }

    private static void SkipPayload(NbtReader reader, NbtTagType type)
    {
        var stream = reader.BaseStream;
        switch (type)
        {
            case NbtTagType.Byte:
                stream.Seek(1, SeekOrigin.Current);
                break;
            case NbtTagType.Short:
                stream.Seek(2, SeekOrigin.Current);
                break;
            case NbtTagType.Int or NbtTagType.Float:
                stream.Seek(4, SeekOrigin.Current);
                break;
            case NbtTagType.Long or NbtTagType.Double:
                stream.Seek(8, SeekOrigin.Current);
                break;
            case NbtTagType.ByteArray:
                stream.Seek(Math.Max(reader.ReadInt32(), 0), SeekOrigin.Current);
                break;
            case NbtTagType.IntArray:
                stream.Seek(Math.Max(reader.ReadInt32(), 0) * 4L, SeekOrigin.Current);
                break;
            case NbtTagType.LongArray:
                stream.Seek(Math.Max(reader.ReadInt32(), 0) * 8L, SeekOrigin.Current);
                break;
            case NbtTagType.String:
                stream.Seek(Math.Max((int)reader.ReadInt16(), 0), SeekOrigin.Current);
                break;
            case NbtTagType.List:
                var (elementType, count) = ReadListHeader(reader);
                for (var i = 0; i < count; i++)
                    SkipPayload(reader, elementType);

                break;
            case NbtTagType.Compound:
                while (true)
                {
                    var fieldType = (NbtTagType)reader.ReadByte();
                    if (fieldType == NbtTagType.End)
                        break;

                    stream.Seek(Math.Max((int)reader.ReadInt16(), 0), SeekOrigin.Current);
                    SkipPayload(reader, fieldType);
                }

                break;
            default:
                throw new InvalidDataException($"Unknown NBT tag type {type}.");
        }
    }

    /// <summary>A block entry of a template: its position, palette index and block entity data.</summary>
    private readonly record struct TemplateBlock(Vector Position, int State, NbtCompound? Nbt);

    /// <summary>Blocks vanilla flags with <c>dynamicShape()</c> (shapes that depend on block entity state).</summary>
    private static bool HasDynamicShape(IBlock block) => block.BlockClass() is "ShulkerBoxBlock" or "MovingPistonBlock";

    private static Vector ReadVector(NbtList list) => new(IntAt(list, 0), IntAt(list, 1), IntAt(list, 2));

    private static int IntAt(NbtList list, int index) => index < list.Count ? ((NbtTag<int>)list[index]).Value : 0;

    private static double DoubleAt(NbtList list, int index) => index < list.Count ? ((NbtTag<double>)list[index]).Value : 0.0;

    /// <summary>
    /// The blocks of one palette, like vanilla's <c>StructureTemplate.Palette</c>, with lazily built per-block views.
    /// </summary>
    private sealed class Palette(List<StructureBlockInfo> blocks)
    {
        private readonly ConcurrentDictionary<Material, List<StructureBlockInfo>> byMaterial = new();

        public List<StructureBlockInfo> Blocks { get; } = blocks;

        private readonly IReadOnlyList<JigsawBlockInfo>?[] rotatedJigsaws = new IReadOnlyList<JigsawBlockInfo>?[4];

        public IReadOnlyList<JigsawBlockInfo> Jigsaws => field ??= [.. this.BlocksOf(Material.Jigsaw).Select(JigsawBlockInfo.Of)];

        /// <summary>The jigsaws rotated around the template's origin, built once per rotation.</summary>
        public IReadOnlyList<JigsawBlockInfo> RotatedJigsaws(StructureRotation rotation) =>
            this.rotatedJigsaws[(int)rotation] ??= [.. this.Jigsaws.Select(jigsaw => jigsaw with
            {
                Info = new StructureBlockInfo(Transform(jigsaw.Info.Position, rotation, Vector.Zero), jigsaw.Info.Block.Rotate(rotation), jigsaw.Info.Nbt)
            })];

        public List<StructureBlockInfo> BlocksOf(Material material) =>
            this.byMaterial.GetOrAdd(material, key => this.Blocks.Where(info => info.Block.Material == key).ToList());
    }

    private readonly record struct EntityPosition(double X, double Y, double Z);

    private sealed record StructureEntityInfo(EntityPosition Position, Vector BlockPosition, NbtCompound Nbt);
}

/// <summary>
/// How a jigsaw block may connect, like vanilla's <c>JigsawBlockEntity.JointType</c>.
/// </summary>
public enum JigsawJointType
{
    /// <summary>The connected jigsaw may be turned around the connection.</summary>
    Rollable,

    /// <summary>The connected jigsaw's top must face the same way.</summary>
    Aligned
}

/// <summary>
/// A jigsaw block of a template, like vanilla's <c>StructureTemplate.JigsawBlockInfo</c>.
/// </summary>
/// <param name="Name">The jigsaw's name, which other jigsaws target.</param>
/// <param name="Pool">The template pool the next piece comes from.</param>
/// <param name="Target">The name of the jigsaw this one connects to.</param>
/// <param name="PlacementPriority">Pieces attached here are expanded before lower priorities.</param>
/// <param name="SelectionPriority">Jigsaws with higher priorities are tried first.</param>
public sealed record JigsawBlockInfo(StructureBlockInfo Info, JigsawJointType JointType, string Name, string Pool, string Target,
    int PlacementPriority, int SelectionPriority)
{
    public const string EmptyId = "minecraft:empty";

    // The front and top directions of each jigsaw orientation, by state id.
    private static readonly Dictionary<int, (BlockFace Front, BlockFace Top)> orientations = CreateOrientations();

    /// <summary>The direction the jigsaw faces (the first half of its <c>orientation</c>).</summary>
    public BlockFace FrontFacing => orientations[this.Info.Block.StateId()].Front;

    /// <summary>The jigsaw's top direction (the second half of its <c>orientation</c>).</summary>
    public BlockFace TopFacing => orientations[this.Info.Block.StateId()].Top;

    public static BlockFace GetFrontFacing(IBlock jigsaw) => orientations[jigsaw.StateId()].Front;

    /// <summary>
    /// Vanilla <c>JigsawBlock.canAttach</c>: the jigsaws face each other, their tops line up unless this one is rollable,
    /// and this one targets the other's name.
    /// </summary>
    public bool CanAttach(JigsawBlockInfo other) =>
        this.FrontFacing == Opposite(other.FrontFacing)
        && (this.JointType == JigsawJointType.Rollable || this.TopFacing == other.TopFacing)
        && this.Target == other.Name;

    /// <summary>Vanilla <c>JigsawBlockInfo.of</c>: reads the jigsaw's settings from its block entity data.</summary>
    internal static JigsawBlockInfo Of(StructureBlockInfo info)
    {
        var nbt = info.Nbt ?? throw new InvalidOperationException($"Jigsaw at {info.Position} has no data.");
        var joint = nbt.TryGetTag<NbtTag<string>>("joint", out var jointTag) && jointTag.Value is "rollable" or "aligned"
            ? jointTag.Value == "rollable" ? JigsawJointType.Rollable : JigsawJointType.Aligned
            : GetFrontFacing(info.Block) is BlockFace.Up or BlockFace.Down ? JigsawJointType.Rollable : JigsawJointType.Aligned;

        return new JigsawBlockInfo(info, joint, ReadId(nbt, "name"), ReadId(nbt, "pool"), ReadId(nbt, "target"),
            nbt.TryGetTag<NbtTag<int>>("placement_priority", out var placement) ? placement.Value : 0,
            nbt.TryGetTag<NbtTag<int>>("selection_priority", out var selection) ? selection.Value : 0);
    }

    private static Dictionary<int, (BlockFace Front, BlockFace Top)> CreateOrientations()
    {
        var result = new Dictionary<int, (BlockFace Front, BlockFace Top)>();
        foreach (var orientation in (ReadOnlySpan<string>)["down_east", "down_north", "down_south", "down_west", "up_east", "up_north", "up_south",
            "up_west", "west_up", "east_up", "north_up", "south_up"])
        {
            var state = BlockStateProperties.GetState("minecraft:jigsaw", new Dictionary<string, string> { ["orientation"] = orientation });
            var parts = orientation.Split('_');
            result[state.StateId()] = (ParseFace(parts[0]), ParseFace(parts[1]));
        }

        return result;
    }

    private static string ReadId(NbtCompound nbt, string name)
    {
        if (!nbt.TryGetTag<NbtTag<string>>(name, out var tag) || string.IsNullOrEmpty(tag.Value))
            return EmptyId;

        return tag.Value.Contains(':') ? tag.Value : "minecraft:" + tag.Value;
    }

    private static BlockFace Opposite(BlockFace face) => face switch
    {
        BlockFace.Up => BlockFace.Down,
        BlockFace.Down => BlockFace.Up,
        BlockFace.North => BlockFace.South,
        BlockFace.South => BlockFace.North,
        BlockFace.East => BlockFace.West,
        _ => BlockFace.East
    };

    private static BlockFace ParseFace(string name) => name switch
    {
        "up" => BlockFace.Up,
        "down" => BlockFace.Down,
        "north" => BlockFace.North,
        "south" => BlockFace.South,
        "east" => BlockFace.East,
        _ => BlockFace.West
    };
}
