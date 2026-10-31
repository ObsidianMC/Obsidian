using Obsidian.Nbt;

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

    internal override string TypeId => "minecraft:jigsaw";

    /// <summary>
    /// Adds vanilla 26.3's <c>PoolElementStructurePiece</c> fields: where the element is placed, its ground level,
    /// the element itself, its rotation and its junctions, and the liquid settings when they aren't the default.
    /// </summary>
    internal override void SaveState(NbtCompound tag)
    {
        base.SaveState(tag);

        var position = this.Position;
        tag.Add(new NbtTag<int>("PosX", position.X));
        tag.Add(new NbtTag<int>("PosY", position.Y));
        tag.Add(new NbtTag<int>("PosZ", position.Z));
        tag.Add(new NbtTag<int>("ground_level_delta", this.GroundLevelDelta));
        tag.Add(this.Element.Save("pool_element"));
        tag.Add(new NbtTag<string>("rotation", RotationName(this.ElementRotation)));

        var junctions = new NbtList(NbtTagType.Compound, "junctions");
        foreach (var junction in this.junctions)
        {
            junctions.Add(new NbtCompound
            {
                new NbtTag<int>("source_x", junction.SourceX),
                new NbtTag<int>("source_ground_y", junction.SourceGroundY),
                new NbtTag<int>("source_z", junction.SourceZ),
                new NbtTag<int>("delta_y", junction.DeltaY),
                new NbtTag<string>("dest_proj", junction.DestinationRigid ? "rigid" : "terrain_matching")
            });
        }

        tag.Add(junctions);

        if (this.LiquidSettings != LiquidSettings.ApplyWaterlogging)
            tag.Add(new NbtTag<string>("liquid_settings", LiquidSettingsName(this.LiquidSettings)));
    }

    internal static string LiquidSettingsName(LiquidSettings settings) =>
        settings == LiquidSettings.IgnoreWaterlogging ? "ignore_waterlogging" : "apply_waterlogging";

    public override void PostProcess(StructurePieceContext context) =>
        this.Element.Place(context, this.Position, context.Pivot, this.ElementRotation, context.Box, context.Random, this.LiquidSettings, false);
}
