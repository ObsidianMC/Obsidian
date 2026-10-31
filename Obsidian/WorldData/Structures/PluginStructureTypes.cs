using Obsidian.API.World.Generator.RandomSources;
using Obsidian.API.World.Structures;
using Obsidian.Nbt;
using Obsidian.WorldData.Generators.Mojang;
using Obsidian.WorldData.Structures.Pools;
using System.Collections.Frozen;

namespace Obsidian.WorldData.Structures;

/// <summary>
/// A structure of a type a plugin registered (a <see cref="CustomStructure"/>): Obsidian reads its common fields,
/// checks its biomes and places its pieces, and the plugin's type decides where it starts and what pieces it has.
/// </summary>
internal sealed class PluginStructure : Structure
{
    /// <summary>
    /// The plugin's structure, read from the file's other keys.
    /// </summary>
    internal CustomStructure Definition { get; set; } = null!;

    internal override StructureStub? FindGenerationPoint(StructureGenerationContext context)
    {
        var start = new PluginStartContext(context);
        if (this.Definition.FindStart(start) is not Vector position)
            return null;

        return new StructureStub(position, builder =>
        {
            var pieces = new List<CustomStructurePiece>();
            this.Definition.AddPieces(start, position, pieces);

            foreach (var piece in pieces)
                builder.AddPiece(new PluginStructurePiece(piece));
        });
    }

    private sealed class PluginStartContext(StructureGenerationContext context) : StructureStartContext
    {
        public override int ChunkX => context.ChunkX;

        public override int ChunkZ => context.ChunkZ;

        public override long Seed => context.Seed;

        public override IRandomSource Random => context.Random;

        public override int MinY => context.MinY;

        public override int Height => context.Height;

        public override int SeaLevel => context.Terrain.SeaLevel;

        public override WorldGenerationContext Generation => context.Generation;

        public override int GetBaseHeight(int x, int z, HeightmapType heightmap) =>
            context.Terrain.GetBaseHeight(x, z, heightmap);
    }
}

/// <summary>
/// A <see cref="CustomStructurePiece"/> among a structure's pieces. Only the fields every piece has are saved; vanilla
/// has no type for it.
/// </summary>
internal sealed class PluginStructurePiece(CustomStructurePiece piece)
    : StructurePiece(0, BlockBox.FromCorners(piece.Bounds.Min, piece.Bounds.Max))
{
    public CustomStructurePiece Piece { get; } = piece;

    internal override string TypeId => "obsidian:plugin_piece";

    public override void PostProcess(StructurePieceContext context)
    {
        var placement = new PluginPieceContext(
            context.Level,
            context.Random,
            context.Box,
            context.Generation,
            this.BoundingBox.Min,
            StructureRotation.None);

        this.Piece.Place(placement);
    }
}

/// <summary>
/// A template pool element of a type a plugin registered (a <see cref="CustomPoolElement"/>), which Obsidian's
/// jigsaw assembly places like vanilla's elements, turned around its origin.
/// </summary>
internal sealed class PluginPoolElement : StructurePoolElement
{
    // Rigid unless the element's projection says otherwise.
    public PluginPoolElement() => this.SetProjection(Projection.Rigid);

    /// <summary>
    /// The plugin's element, read from the element's other keys.
    /// </summary>
    internal CustomPoolElement Element { get; set; } = null!;

    // The values of a jigsaw block's orientation.
    private static readonly FrozenSet<string> orientations = FrozenSet.Create(
        "down_east",
        "down_north",
        "down_south",
        "down_west",
        "up_east",
        "up_north",
        "up_south",
        "up_west",
        "west_up",
        "east_up",
        "north_up",
        "south_up");

    // Elements are stateless, so their jigsaws are asked for once, as the jigsaw blocks a template would hold.
    private StructureBlockInfo[] Jigsaws => field ??= [.. this.Element.GetJigsaws().Select(ToBlockInfo)];

    private Vector Size
    {
        get
        {
            var size = this.Element.Size;
            return new Vector(Math.Max(1, size.X), Math.Max(1, size.Y), Math.Max(1, size.Z));
        }
    }

    public override Vector GetSize(StructureRotation rotation)
    {
        var size = this.Size;
        return rotation is StructureRotation.Clockwise90 or StructureRotation.CounterClockwise90
            ? new Vector(size.Z, size.Y, size.X)
            : size;
    }

    public override List<JigsawBlockInfo> GetShuffledJigsawBlocks(
        Vector position,
        StructureRotation rotation,
        IRandomSource random)
    {
        var jigsaws = new List<JigsawBlockInfo>(this.Jigsaws.Length);

        foreach (var jigsaw in this.Jigsaws)
        {
            var turned = new StructureBlockInfo(
                StructureTemplate.Transform(jigsaw.Position, rotation, Vector.Zero) + position,
                jigsaw.Block.Rotate(rotation),
                jigsaw.Nbt);

            jigsaws.Add(JigsawBlockInfo.Of(turned));
        }

        ShuffleJigsaws(jigsaws, random);
        return jigsaws;
    }

    // A jigsaw block's state and data, as a structure block saves them.
    private static StructureBlockInfo ToBlockInfo(PoolJigsaw jigsaw)
    {
        if (!orientations.Contains(jigsaw.Orientation))
            throw new ArgumentException($"{jigsaw.Orientation} isn't a jigsaw's orientation.");

        var properties = new Dictionary<string, string> { ["orientation"] = jigsaw.Orientation };
        var block = BlockStateProperties.GetState("minecraft:jigsaw", properties);

        var data = new NbtCompound
        {
            new NbtTag<string>("name", jigsaw.Name),
            new NbtTag<string>("target", jigsaw.Target),
            new NbtTag<string>("pool", jigsaw.Pool),
            new NbtTag<string>("joint", jigsaw.Rollable ? "rollable" : "aligned"),
            new NbtTag<string>("final_state", "minecraft:air")
        };

        return new StructureBlockInfo(jigsaw.Position, block, data);
    }

    public override BlockBox GetBoundingBox(Vector position, StructureRotation rotation)
    {
        var first = StructureTemplate.Transform(Vector.Zero, rotation, Vector.Zero);
        var last = StructureTemplate.Transform(this.Size - 1, rotation, Vector.Zero);
        return BlockBox.FromCorners(first, last).Move(position);
    }

    public override bool Place(
        StructurePieceContext context,
        Vector position,
        Vector pivot,
        StructureRotation rotation,
        BlockBox box,
        IRandomSource random,
        LiquidSettings liquidSettings,
        bool keepJigsaws)
    {
        var placement = new PluginPieceContext(context.Level, random, box, context.Generation, position, rotation);
        return this.Element.Place(placement);
    }
}

/// <summary>
/// What a plugin's piece or pool element sees while it's placed.
/// </summary>
internal sealed class PluginPieceContext(
    IWorldGenLevel level,
    IRandomSource random,
    BlockBox box,
    WorldGenerationContext generation,
    Vector origin,
    StructureRotation rotation) : CustomPieceContext
{
    public override IWorldGenLevel Level => level;

    public override IRandomSource Random => random;

    public override BlockArea Box => new(box.Min, box.Max);

    public override WorldGenerationContext Generation => generation;

    public override Vector Origin => origin;

    // The API's rotations are declared in the same order as Obsidian's.
    public override PieceRotation Rotation => (PieceRotation)rotation;

    public override Vector ToWorld(Vector offset) =>
        StructureTemplate.Transform(offset, rotation, Vector.Zero) + origin;

    public override bool SetBlock(Vector offset, IBlock block)
    {
        var position = this.ToWorld(offset);
        if (!box.IsInside(position))
            return false;

        var turned = block.Rotate(rotation);
        if (!level.SetBlock(position, turned))
            return false;

        if (turned.HasFluid())
            level.ScheduleFluidTick(position);

        return true;
    }
}
