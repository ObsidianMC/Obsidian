namespace Obsidian.WorldData.Structures.Pools;

/// <summary>
/// A placed pool element of a jigsaw structure, like vanilla's <c>PoolElementStructurePiece</c>.
/// </summary>
public sealed class PoolElementStructurePiece : StructurePiece, IJigsawPiece
{
    private readonly List<JigsawJunction> junctions = [];

    // The position moves with the bounding box (StructurePiece.Move), so it's kept relative to the box.
    private readonly Vector positionOffset;

    /// <param name="position">Where the element's template origin is placed.</param>
    /// <param name="groundLevelDelta">How far above the piece's bottom the ground is meant to be.</param>
    public PoolElementStructurePiece(StructurePoolElement element, Vector position, int groundLevelDelta, StructureRotation rotation,
        BlockBox boundingBox, LiquidSettings liquidSettings) : base(0, boundingBox)
    {
        this.Element = element;
        this.positionOffset = position - boundingBox.Min;
        this.GroundLevelDelta = groundLevelDelta;
        this.ElementRotation = rotation;
        this.LiquidSettings = liquidSettings;
    }

    public StructurePoolElement Element { get; }

    public Vector Position => this.BoundingBox.Min + this.positionOffset;

    /// <summary>The element's rotation (vanilla's <c>getRotation</c> for these pieces).</summary>
    public StructureRotation ElementRotation { get; }

    public LiquidSettings LiquidSettings { get; }

    public int GroundLevelDelta { get; }

    public bool IsRigid => this.Element.Projection == Projection.Rigid;

    public IReadOnlyList<JigsawJunction> Junctions => this.junctions;

    public void AddJunction(JigsawJunction junction) => this.junctions.Add(junction);

    public override void PostProcess(StructurePieceContext context) =>
        this.Element.Place(context, this.Position, context.Pivot, this.ElementRotation, context.Box, context.Random, this.LiquidSettings, false);
}
