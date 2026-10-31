using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Nbt;

namespace Obsidian.WorldData.Structures;

/// <summary>
/// A piece made of one structure template, like vanilla's <c>TemplateStructurePiece</c> (igloos, shipwrecks, ocean
/// ruins, ruined portals, nether fossils, end cities, woodland mansions).
/// </summary>
public abstract class TemplateStructurePiece : StructurePiece
{
    // The template position moves with the bounding box (StructurePiece.Move), so it's kept relative to the box.
    private Vector templateOffset;

    // Where the template was last placed in this session, which vanilla's piece keeps its box at (see PlaceTemplate).
    private Vector? placedAt;

    /// <param name="templateId">The template id, e.g. <c>minecraft:igloo/top</c>; also the piece's template name.</param>
    /// <param name="settings">How the template is placed; copied for each placement, so it isn't changed afterwards.</param>
    /// <param name="templatePosition">Where the template's origin is placed.</param>
    protected TemplateStructurePiece(int genDepth, string templateId, StructurePlaceSettings settings, Vector templatePosition)
        : this(genDepth, templateId, templateId, settings, templatePosition)
    {
    }

    /// <param name="templateName">The name vanilla saves for the piece when it differs from the id (end cities).</param>
    protected TemplateStructurePiece(int genDepth, string templateId, string templateName, StructurePlaceSettings settings, Vector templatePosition)
        : this(genDepth, StructureRegistry.Get(templateId), templateName, settings, templatePosition)
    {
    }

    private TemplateStructurePiece(int genDepth, StructureTemplate template, string templateName,
        StructurePlaceSettings settings, Vector templatePosition)
        : base(genDepth, template.GetBoundingBox(settings, templatePosition))
    {
        this.Orientation = BlockFace.North;
        this.TemplateName = templateName;
        this.Template = template;
        this.PlaceSettings = settings;
        this.templateOffset = templatePosition - this.BoundingBox.Min;
    }

    public string TemplateName { get; }

    public StructureTemplate Template { get; }

    public StructurePlaceSettings PlaceSettings { get; }

    public Vector TemplatePosition => this.BoundingBox.Min + this.templateOffset;

    /// <summary>
    /// The template position vanilla saves (<c>TPX</c>, <c>TPY</c>, <c>TPZ</c>), where its piece last placed the
    /// template.
    /// </summary>
    private protected virtual Vector SavedTemplatePosition => this.TemplatePosition;

    /// <summary>
    /// Where the template was last placed in this session, or null. Vanilla's pieces move their box there when
    /// placing, and some (shipwrecks, ocean ruins) their template position too.
    /// </summary>
    private protected Vector? LastPlacement => this.placedAt;

    // Vanilla's TemplateStructurePiece.postProcess sets the box to the template's at the position it places it at.
    private protected override BlockBox SavedBoundingBox =>
        this.placedAt is Vector position
            ? this.Template.GetBoundingBox(this.PlaceSettings, position)
            : this.BoundingBox;

    public override void PostProcess(StructurePieceContext context) => this.PlaceTemplate(context, context.Box, this.TemplatePosition);

    /// <summary>
    /// Adds vanilla's <c>TemplateStructurePiece</c> fields: the template position and name. Subclasses add how the
    /// template is placed (<c>Rot</c>...).
    /// </summary>
    internal override void SaveState(NbtCompound tag)
    {
        base.SaveState(tag);

        var position = this.SavedTemplatePosition;
        tag.Add(new NbtTag<int>("TPX", position.X));
        tag.Add(new NbtTag<int>("TPY", position.Y));
        tag.Add(new NbtTag<int>("TPZ", position.Z));
        tag.Add(new NbtTag<string>("Template", this.TemplateName));
    }

    /// <summary>
    /// Restores the box, and the template position, which vanilla doesn't move with the box when placing moves it
    /// (igloos). Saves without it keep the template where it was relative to the box.
    /// </summary>
    internal override void LoadState(NbtCompound tag)
    {
        base.LoadState(tag);

        if (tag.TryGetTag<NbtTag<int>>("TPX", out var x)
            && tag.TryGetTag<NbtTag<int>>("TPY", out var y)
            && tag.TryGetTag<NbtTag<int>>("TPZ", out var z))
        {
            this.templateOffset = new Vector(x.Value, y.Value, z.Value) - this.BoundingBox.Min;
        }
    }

    /// <summary>
    /// Adds vanilla's <c>Rot</c>, the template's rotation, which most template pieces save.
    /// </summary>
    private protected void SaveRotation(NbtCompound tag) =>
        tag.Add(new NbtTag<string>("Rot", RotationName(this.PlaceSettings.Rotation)));

    /// <summary>
    /// Vanilla's <c>Mirror</c> names.
    /// </summary>
    private protected static string MirrorName(StructureMirror mirror) => mirror switch
    {
        StructureMirror.LeftRight => "LEFT_RIGHT",
        StructureMirror.FrontBack => "FRONT_BACK",
        _ => "NONE"
    };

    /// <summary>
    /// Moves the template without moving the bounding box, like vanilla pieces assigning their template position. Only
    /// call it while the structure is built.
    /// </summary>
    protected void SetTemplatePosition(Vector position) => this.templateOffset = position - this.BoundingBox.Min;

    /// <summary>
    /// Vanilla <c>TemplateStructurePiece.postProcess</c>: places the template at <paramref name="templatePosition"/> inside
    /// <paramref name="box"/>, then handles its data markers and turns its jigsaw blocks into their final state.
    /// </summary>
    /// <remarks>
    /// Vanilla pieces that move or grow the box for one placement change their own fields and the chunk's shared box;
    /// pieces are shared between chunks generating in parallel here, so they pass the values instead.
    /// </remarks>
    protected void PlaceTemplate(StructurePieceContext context, BlockBox box, Vector templatePosition)
    {
        // Placing runs under the start's lock, as saving does (see StructureStart.PlaceInChunk).
        this.placedAt = templatePosition;

        var settings = this.PlaceSettings.Copy();
        settings.BoundingBox = box;
        var level = context.Level;

        if (!this.Template.PlaceInWorld(level, templatePosition, context.Pivot, settings, context.Random))
            return;

        foreach (var marker in this.Template.FilterBlocks(templatePosition, settings, BlockIds.StructureBlock))
        {
            if (marker.Nbt is not null && marker.Nbt.TryGetTag<NbtTag<string>>("mode", out var mode) && mode.Value == "DATA")
            {
                var metadata = marker.Nbt.TryGetTag<NbtTag<string>>("metadata", out var tag) ? tag.Value ?? string.Empty : string.Empty;
                this.HandleDataMarker(metadata, marker.Position, context, box);
            }
        }

        foreach (var jigsaw in this.Template.FilterBlocks(templatePosition, settings, BlockIds.Jigsaw))
        {
            if (jigsaw.Nbt is null)
                continue;

            var finalState = jigsaw.Nbt.TryGetTag<NbtTag<string>>("final_state", out var tag) ? tag.Value! : "minecraft:air";
            level.SetBlock(jigsaw.Position, BlockStateParser.TryParse(finalState) ?? BlocksRegistry.Air);
        }
    }

    /// <summary>
    /// Vanilla <c>handleDataMarker</c>: a structure block in data mode, at its world position, marks where to add a chest,
    /// a mob or the like.
    /// </summary>
    /// <param name="box">The box the template was placed in.</param>
    protected abstract void HandleDataMarker(string metadata, Vector position, StructurePieceContext context, BlockBox box);

    /// <summary>
    /// Vanilla <c>ChestBlockEntity.setLootTable(table, random.nextLong())</c>: gives the chest at <paramref name="position"/>
    /// a loot table; the seed is only drawn when there's a chest (or trapped chest) block entity.
    /// </summary>
    protected static void SetChestLootTable(IWorldGenLevel level, IRandomSource random, Vector position, string lootTable)
    {
        var chest = level.GetBlockEntity(position);
        if (chest?.Id is not ("minecraft:chest" or "minecraft:trapped_chest"))
            return;

        BlockEntityNbt.SetLootTable(chest, lootTable, random.NextLong());
    }
}
