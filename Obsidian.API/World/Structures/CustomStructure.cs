using Obsidian.API.World.Features;
using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.API.World.Structures;

/// <summary>
/// A structure type a plugin adds in code, like a NeoForge mod's structure type: structure files name it in their
/// <c>type</c>. Register it with <c>RegisterWorldgenType&lt;CustomStructure, T&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// Obsidian reads a structure's common fields (<c>biomes</c>, <c>spawn_overrides</c>, <c>step</c> and
/// <c>terrain_adaptation</c>) itself, checks the biome at the start, and places the pieces; the file's other keys set
/// the type's properties, read like a feature type's.
/// </para>
/// <para>
/// Instances come from structure files and are shared between threads, so keep them stateless. Starts aren't saved
/// with their pieces: they're made again from the seed when a chunk needs them, also after a restart, so a start must
/// depend only on its context.
/// </para>
/// </remarks>
public abstract class CustomStructure
{
    /// <summary>
    /// Where the structure starts in the context's chunk, or <c>null</c> when it doesn't start there. The structure
    /// only starts when the biome there is one of its <c>biomes</c>.
    /// </summary>
    public abstract Vector? FindStart(StructureStartContext context);

    /// <summary>
    /// Adds the structure's pieces, once it starts at <paramref name="start"/>. A structure without pieces doesn't
    /// start. Called with the same context, so its random goes on from <see cref="FindStart"/>'s.
    /// </summary>
    public abstract void AddPieces(
        StructureStartContext context,
        Vector start,
        ICollection<CustomStructurePiece> pieces);
}

/// <summary>
/// A piece of a <see cref="CustomStructure"/>: an area of blocks, placed by each chunk it reaches as that chunk is
/// decorated.
/// </summary>
/// <param name="bounds">The blocks the piece may place, which decide the chunks that place it.</param>
public abstract class CustomStructurePiece(BlockArea bounds)
{
    /// <summary>
    /// The blocks the piece may place.
    /// </summary>
    public BlockArea Bounds { get; } = bounds;

    /// <summary>
    /// Places the part of the piece in <see cref="CustomPieceContext.Box"/>. The context's
    /// <see cref="CustomPieceContext.Origin"/> is the piece's lowest corner, unrotated.
    /// </summary>
    public abstract void Place(CustomPieceContext context);
}

/// <summary>
/// A pool element type a plugin adds in code, like a NeoForge mod's pool element type: template pools name it in an
/// element's <c>element_type</c>. Register it with <c>RegisterWorldgenType&lt;CustomPoolElement, T&gt;</c>.
/// </summary>
/// <remarks>
/// Obsidian reads the element's <c>projection</c> (<c>rigid</c> by default) itself; its other keys set the type's
/// properties, read like a feature type's. Elements are shared between threads, so keep them stateless.
/// </remarks>
public abstract class CustomPoolElement
{
    /// <summary>
    /// The element's size from its origin, before it's turned: the blocks it may place, which jigsaw assembly keeps
    /// apart from other pieces. At least one block each way.
    /// </summary>
    public virtual Vector Size => new(1, 1, 1);

    /// <summary>
    /// The jigsaws that connect the element to other pieces, at positions from its origin before it's turned. By
    /// default, like vanilla's feature elements, one facing down at the origin named <c>minecraft:bottom</c>, so the
    /// element hangs off any jigsaw targeting that name.
    /// </summary>
    /// <remarks>Jigsaws only connect pieces; they aren't placed.</remarks>
    public virtual IReadOnlyList<PoolJigsaw> GetJigsaws() => [new(Vector.Zero, "down_south", "minecraft:bottom")];

    /// <summary>
    /// Places the part of the element in <see cref="CustomPieceContext.Box"/>, with its origin at
    /// <see cref="CustomPieceContext.Origin"/> turned by <see cref="CustomPieceContext.Rotation"/>.
    /// </summary>
    /// <returns>Whether the element could be placed; jigsaw assembly ignores it, as vanilla does.</returns>
    public abstract bool Place(CustomPieceContext context);
}

/// <summary>
/// A jigsaw of a <see cref="CustomPoolElement"/>, like a jigsaw block a structure block saves in a template.
/// </summary>
/// <param name="Position">Where it is from the element's origin, before the element is turned.</param>
/// <param name="Orientation">The <c>minecraft:jigsaw</c> block's <c>orientation</c>, like <c>north_up</c> or
/// <c>up_north</c>: the way it faces, then its top.</param>
/// <param name="Name">The name other pieces' jigsaws target to attach to this one.</param>
/// <param name="Target">The name of the jigsaw a piece attached here attaches by.</param>
/// <param name="Pool">The template pool the piece attached here is picked from.</param>
/// <param name="Rollable">Whether a piece attached here may turn around the jigsaw's facing (vanilla's
/// <c>rollable</c> joint), rather than keep its top lined up (<c>aligned</c>).</param>
public readonly record struct PoolJigsaw(
    Vector Position,
    string Orientation,
    string Name,
    string Target = "minecraft:empty",
    string Pool = "minecraft:empty",
    bool Rollable = true);

/// <summary>
/// What a <see cref="CustomStructure"/> sees while it decides whether and how it starts in a chunk, before any chunk
/// exists. Only the server creates contexts.
/// </summary>
public abstract class StructureStartContext
{
    internal StructureStartContext()
    {
    }

    public abstract int ChunkX { get; }

    public abstract int ChunkZ { get; }

    /// <summary>
    /// The world seed.
    /// </summary>
    public abstract long Seed { get; }

    /// <summary>
    /// Seeded from the seed and the chunk, like vanilla's structures' random, so a start is the same every time.
    /// </summary>
    public abstract IRandomSource Random { get; }

    /// <summary>
    /// The dimension's lowest Y.
    /// </summary>
    public abstract int MinY { get; }

    /// <summary>
    /// The dimension's height.
    /// </summary>
    public abstract int Height { get; }

    public abstract int SeaLevel { get; }

    /// <summary>
    /// The vertical range height providers and vertical anchors resolve in.
    /// </summary>
    public abstract WorldGenerationContext Generation { get; }

    /// <summary>
    /// The first free Y above the highest block matching <paramref name="heightmap"/> at a column, from the terrain's
    /// noise alone (structures start before chunks are generated).
    /// </summary>
    public abstract int GetBaseHeight(int x, int z, HeightmapType heightmap);
}

/// <summary>
/// What a <see cref="CustomStructurePiece"/> or <see cref="CustomPoolElement"/> sees while it's placed in the chunk
/// being decorated. Only the server creates contexts.
/// </summary>
public abstract class CustomPieceContext
{
    internal CustomPieceContext()
    {
    }

    /// <summary>
    /// The chunks around the chunk being decorated, in world coordinates.
    /// </summary>
    public abstract IWorldGenLevel Level { get; }

    /// <summary>
    /// Seeded for the structure and the chunk being decorated.
    /// </summary>
    public abstract IRandomSource Random { get; }

    /// <summary>
    /// The part of the chunk being decorated that the piece may place in; each chunk the piece reaches places its own
    /// part.
    /// </summary>
    public abstract BlockArea Box { get; }

    /// <summary>
    /// The vertical range height providers and vertical anchors resolve in.
    /// </summary>
    public abstract WorldGenerationContext Generation { get; }

    /// <summary>
    /// The world position offsets are from: a piece's lowest corner, or a pool element's origin.
    /// </summary>
    public abstract Vector Origin { get; }

    /// <summary>
    /// How a pool element is turned around its origin; <see cref="PieceRotation.None"/> for a piece.
    /// </summary>
    public abstract PieceRotation Rotation { get; }

    /// <summary>
    /// The world position of an offset from <see cref="Origin"/>, turned by <see cref="Rotation"/>.
    /// </summary>
    public abstract Vector ToWorld(Vector offset);

    /// <summary>
    /// Places a block at an offset from <see cref="Origin"/>, turned with the piece by <see cref="Rotation"/>, when
    /// it's in <see cref="Box"/>.
    /// </summary>
    /// <returns>Whether the block was placed.</returns>
    public abstract bool SetBlock(Vector offset, IBlock block);
}

/// <summary>
/// A box of blocks, from <see cref="Min"/> to <see cref="Max"/> inclusive.
/// </summary>
public readonly record struct BlockArea(Vector Min, Vector Max)
{
    /// <summary>
    /// Whether <paramref name="position"/> is in the box.
    /// </summary>
    public bool Contains(Vector position) =>
        position.X >= this.Min.X
        && position.X <= this.Max.X
        && position.Y >= this.Min.Y
        && position.Y <= this.Max.Y
        && position.Z >= this.Min.Z
        && position.Z <= this.Max.Z;
}

/// <summary>
/// How a structure's pool element is turned around its origin, clockwise seen from above.
/// </summary>
public enum PieceRotation
{
    None,
    Clockwise90,
    Clockwise180,
    CounterClockwise90
}
