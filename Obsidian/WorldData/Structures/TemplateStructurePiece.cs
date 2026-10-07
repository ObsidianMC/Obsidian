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

    public override void PostProcess(StructurePieceContext context) => this.PlaceTemplate(context, context.Box, this.TemplatePosition);

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
        var settings = this.PlaceSettings.Copy();
        settings.BoundingBox = box;
        var level = context.Level;

        if (!this.Template.PlaceInWorld(level, templatePosition, context.Pivot, settings, context.Random))
            return;

        foreach (var marker in this.Template.FilterBlocks(templatePosition, settings, Material.StructureBlock))
        {
            if (marker.Nbt is not null && marker.Nbt.TryGetTag<NbtTag<string>>("mode", out var mode) && mode.Value == "DATA")
            {
                var metadata = marker.Nbt.TryGetTag<NbtTag<string>>("metadata", out var tag) ? tag.Value ?? string.Empty : string.Empty;
                this.HandleDataMarker(metadata, marker.Position, context, box);
            }
        }

        foreach (var jigsaw in this.Template.FilterBlocks(templatePosition, settings, Material.Jigsaw))
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
        var chest = level.GetBlockEntity(position) as DataBlockEntity;
        if (chest?.Id is not ("minecraft:chest" or "minecraft:trapped_chest"))
            return;

        chest.Set("LootTable", lootTable);
        var seed = random.NextLong();
        if (seed != 0L)
            chest.Set("LootTableSeed", seed);
        else
            chest.Data.Remove("LootTableSeed");
    }
}
