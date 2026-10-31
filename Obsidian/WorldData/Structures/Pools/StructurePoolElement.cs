using Obsidian.API.World.Generator.RandomSources;
using Obsidian.API.World.Structures;
using Obsidian.Nbt;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Structures.Processors;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Obsidian.WorldData.Structures.Pools;

/// <summary>
/// A piece a <see cref="StructureTemplatePool"/> can pick, like vanilla's <c>StructurePoolElement</c>.
/// </summary>
/// <remarks>Elements come from pool data and are shared between threads; they're immutable once built.</remarks>
public abstract class StructurePoolElement
{
    private Projection projection;

    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// How the element sits on the terrain. A <see cref="ListPoolElement"/> passes its projection on to its elements.
    /// </summary>
    public Projection Projection { get => this.projection; init => this.SetProjection(value); }

    /// <summary>
    /// Vanilla <c>getGroundLevelDelta</c>: how far above the element's bottom the ground is meant to be.
    /// </summary>
    public virtual int GroundLevelDelta => 1;

    public abstract Vector GetSize(StructureRotation rotation);

    /// <summary>
    /// Saves the element in the format of vanilla 26.3's <c>StructurePoolElement</c> codec (as jigsaw pieces save
    /// their <c>pool_element</c>): its <c>element_type</c> and its type's fields.
    /// </summary>
    internal NbtCompound Save(string? name)
    {
        var tag = new NbtCompound(name) { new NbtTag<string>("element_type", this.Type) };
        this.SaveFields(tag);
        return tag;
    }

    /// <summary>
    /// A saved element (see <see cref="Save"/>) with each placed feature defined inline replaced by a marker, for
    /// comparing saves: Obsidian saves the definition it read, which vanilla's codecs write normalised (defaults left
    /// out, numbers typed), so the same element may be saved differently. Everything else is kept.
    /// </summary>
    internal static NbtCompound WithoutInlineFeatures(NbtCompound element)
    {
        if (element.TryGetTag<NbtCompound>("feature", out _))
        {
            var copy = NbtCopy.Copy(element, "feature");
            copy.Add(new NbtTag<string>("feature", "<inline>"));
            return copy;
        }

        if (element.TryGetTag<NbtList>("elements", out var elements))
        {
            var copy = NbtCopy.Copy(element, "elements");
            var list = new NbtList(NbtTagType.Compound, "elements");
            foreach (var child in elements)
                list.Add(child is NbtCompound compound ? WithoutInlineFeatures(compound) : child);

            copy.Add(list);
            return copy;
        }

        return element;
    }

    /// <summary>
    /// Adds the element's fields; by default its projection.
    /// </summary>
    private protected virtual void SaveFields(NbtCompound tag) =>
        tag.Add(new NbtTag<string>("projection", this.Projection == Projection.Rigid ? "rigid" : "terrain_matching"));

    /// <summary>
    /// Vanilla <c>getShuffledJigsawBlocks</c>: the element's jigsaws placed at <paramref name="position"/>, in a random
    /// order, highest selection priority first.
    /// </summary>
    public abstract List<JigsawBlockInfo> GetShuffledJigsawBlocks(Vector position, StructureRotation rotation, IRandomSource random);

    /// <summary>
    /// <see cref="GetShuffledJigsawBlocks(Vector, StructureRotation, IRandomSource)"/> into <paramref name="destination"/>,
    /// replacing its contents, for callers that reuse a list.
    /// </summary>
    public virtual void GetShuffledJigsawBlocks(Vector position, StructureRotation rotation, IRandomSource random, List<JigsawBlockInfo> destination)
    {
        destination.Clear();
        destination.AddRange(this.GetShuffledJigsawBlocks(position, rotation, random));
    }

    public abstract BlockBox GetBoundingBox(Vector position, StructureRotation rotation);

    /// <summary>
    /// Places the part of the element inside <paramref name="box"/>.
    /// </summary>
    /// <param name="pivot">The pivot passed to template processors.</param>
    /// <param name="keepJigsaws">Keeps the jigsaw blocks instead of turning them into their final state.</param>
    public abstract bool Place(StructurePieceContext context, Vector position, Vector pivot, StructureRotation rotation, BlockBox box,
        IRandomSource random, LiquidSettings liquidSettings, bool keepJigsaws);

    /// <summary>
    /// Vanilla's order of a template's jigsaws: shuffled, then highest selection priority first.
    /// </summary>
    internal static void ShuffleJigsaws(List<JigsawBlockInfo> destination, IRandomSource random)
    {
        var jigsaws = CollectionsMarshal.AsSpan(destination);
        FeatureHelpers.Shuffle(jigsaws, random);

        // Java's List.sort is stable: an insertion sort by descending selection priority keeps the shuffled order
        // of ties.
        for (var i = 1; i < jigsaws.Length; i++)
        {
            var jigsaw = jigsaws[i];
            var j = i - 1;
            for (; j >= 0 && jigsaws[j].SelectionPriority < jigsaw.SelectionPriority; j--)
                jigsaws[j + 1] = jigsaws[j];

            jigsaws[j + 1] = jigsaw;
        }
    }

    /// <summary>Vanilla <c>setProjection</c>, which list elements use to pass their projection on.</summary>
    internal virtual void SetProjection(Projection value) => this.projection = value;

    /// <summary>The processors a projection adds: terrain matching pieces follow the surface.</summary>
    internal static IReadOnlyList<StructureProcessor> ProjectionProcessors(Projection projection) =>
        projection == Projection.TerrainMatching ? terrainMatchingProcessors : [];

    private static readonly StructureProcessor[] terrainMatchingProcessors =
        [new GravityProcessor { Type = "minecraft:gravity", Heightmap = HeightmapType.WorldSurfaceWG, Offset = -1 }];
}

/// <summary>
/// A structure template, like vanilla's <c>SinglePoolElement</c>.
/// </summary>
[StructureType("minecraft:single_pool_element")]
public class SinglePoolElement : StructurePoolElement
{
    /// <summary>The template id, e.g. <c>minecraft:village/plains/houses/plains_small_house_1</c>.</summary>
    public required string Location { get; init; }

    public required StructureProcessorList Processors { get; init; }

    /// <summary>Replaces the structure's liquid settings for this element when set.</summary>
    public LiquidSettings? OverrideLiquidSettings { get; init; }

    /// <summary>
    /// The template, looked up each time, as plugins and data packs may override it.
    /// </summary>
    public StructureTemplate Template => StructureRegistry.Get(this.Location);

    public override Vector GetSize(StructureRotation rotation) => this.Template.GetSize(rotation);

    // A list defined inline is saved without its processors: Obsidian doesn't keep them in vanilla's format. Vanilla's
    // own pools only define empty lists inline.
    private protected override void SaveFields(NbtCompound tag)
    {
        tag.Add(new NbtTag<string>("location", this.Location));

        if (this.Processors.Identifier.Length > 0)
            tag.Add(new NbtTag<string>("processors", this.Processors.Identifier));
        else
            tag.Add(new NbtCompound("processors") { new NbtList(NbtTagType.End, "processors") });

        base.SaveFields(tag);

        if (this.OverrideLiquidSettings is LiquidSettings liquid)
        {
            var name = PoolElementStructurePiece.LiquidSettingsName(liquid);
            tag.Add(new NbtTag<string>("override_liquid_settings", name));
        }
    }

    public override List<JigsawBlockInfo> GetShuffledJigsawBlocks(Vector position, StructureRotation rotation, IRandomSource random)
    {
        var jigsaws = new List<JigsawBlockInfo>();
        this.GetShuffledJigsawBlocks(position, rotation, random, jigsaws);
        return jigsaws;
    }

    public override void GetShuffledJigsawBlocks(Vector position, StructureRotation rotation, IRandomSource random, List<JigsawBlockInfo> destination)
    {
        destination.Clear();
        this.Template.AddJigsaws(destination, position, rotation);
        ShuffleJigsaws(destination, random);
    }

    public override BlockBox GetBoundingBox(Vector position, StructureRotation rotation) => this.Template.GetBoundingBox(position, rotation);

    public override bool Place(StructurePieceContext context, Vector position, Vector pivot, StructureRotation rotation, BlockBox box,
        IRandomSource random, LiquidSettings liquidSettings, bool keepJigsaws) =>
        // Vanilla also runs data markers (structure blocks in data mode) through the processors, but every single element
        // drops structure blocks, so nothing ever reaches them.
        this.Template.PlaceInWorld(context.Level, position, pivot, this.GetSettings(rotation, box, liquidSettings, keepJigsaws), random);

    /// <summary>Vanilla <c>getSettings</c>, in vanilla's processor order.</summary>
    protected virtual StructurePlaceSettings GetSettings(StructureRotation rotation, BlockBox box, LiquidSettings liquidSettings, bool keepJigsaws)
    {
        var settings = new StructurePlaceSettings
        {
            BoundingBox = box,
            Rotation = rotation,
            KnownShape = true,
            IgnoreEntities = false,
            FinalizeEntities = true,
            LiquidSettings = this.OverrideLiquidSettings ?? liquidSettings
        };

        settings.Processors.Add(BlockIgnoreProcessor.StructureBlock);
        if (!keepJigsaws)
            settings.Processors.Add(JigsawReplacementProcessor.Instance);

        settings.Processors.AddRange(this.Processors.Processors.AsSpan());
        settings.Processors.AddRange(ProjectionProcessors(this.Projection));
        return settings;
    }
}

/// <summary>
/// Like <see cref="SinglePoolElement"/>, but the template's air isn't placed, like vanilla's <c>LegacySinglePoolElement</c>.
/// </summary>
[StructureType("minecraft:legacy_single_pool_element")]
public sealed class LegacySinglePoolElement : SinglePoolElement
{
    protected override StructurePlaceSettings GetSettings(StructureRotation rotation, BlockBox box, LiquidSettings liquidSettings, bool keepJigsaws)
    {
        // Vanilla removes the structure block filter and appends one for air and structure blocks, so it runs last.
        var settings = base.GetSettings(rotation, box, liquidSettings, keepJigsaws);
        settings.Processors.Remove(BlockIgnoreProcessor.StructureBlock);
        settings.Processors.Add(BlockIgnoreProcessor.StructureAndAir);
        return settings;
    }
}

/// <summary>
/// Several elements placed at the same position, like vanilla's <c>ListPoolElement</c>. The first element's jigsaws are used.
/// </summary>
[StructureType("minecraft:list_pool_element")]
public sealed class ListPoolElement : StructurePoolElement
{
    public required ImmutableArray<StructurePoolElement> Elements
    {
        get;
        init
        {
            field = value.Length > 0 ? value : throw new ArgumentException("Elements are empty.", nameof(value));
            foreach (var element in value)
                element.SetProjection(this.Projection);
        }
    }

    private protected override void SaveFields(NbtCompound tag)
    {
        var elements = new NbtList(NbtTagType.Compound, "elements");
        foreach (var element in this.Elements)
            elements.Add(element.Save(null));

        tag.Add(elements);
        base.SaveFields(tag);
    }

    internal override void SetProjection(Projection value)
    {
        base.SetProjection(value);

        // The elements aren't set yet when an object initializer sets the projection first.
        if (this.Elements.IsDefault)
            return;

        foreach (var element in this.Elements)
            element.SetProjection(value);
    }

    public override Vector GetSize(StructureRotation rotation)
    {
        var size = Vector.Zero;
        foreach (var element in this.Elements)
        {
            var elementSize = element.GetSize(rotation);
            size = new Vector(Math.Max(size.X, elementSize.X), Math.Max(size.Y, elementSize.Y), Math.Max(size.Z, elementSize.Z));
        }

        return size;
    }

    public override List<JigsawBlockInfo> GetShuffledJigsawBlocks(Vector position, StructureRotation rotation, IRandomSource random) =>
        this.Elements[0].GetShuffledJigsawBlocks(position, rotation, random);

    public override void GetShuffledJigsawBlocks(Vector position, StructureRotation rotation, IRandomSource random,
        List<JigsawBlockInfo> destination) =>
        this.Elements[0].GetShuffledJigsawBlocks(position, rotation, random, destination);

    public override BlockBox GetBoundingBox(Vector position, StructureRotation rotation) =>
        BlockBox.Encapsulating(this.Elements
            .Where(element => element is not EmptyPoolElement)
            .Select(element => element.GetBoundingBox(position, rotation)))
            ?? throw new InvalidOperationException("Unable to calculate the bounding box of a list pool element.");

    public override bool Place(StructurePieceContext context, Vector position, Vector pivot, StructureRotation rotation, BlockBox box,
        IRandomSource random, LiquidSettings liquidSettings, bool keepJigsaws)
    {
        foreach (var element in this.Elements)
        {
            if (!element.Place(context, position, pivot, rotation, box, random, liquidSettings, keepJigsaws))
                return false;
        }

        return true;
    }
}

/// <summary>
/// A placed feature (hay piles, trees...), like vanilla's <c>FeaturePoolElement</c>. It has one rollable jigsaw facing down.
/// </summary>
[StructureType("minecraft:feature_pool_element")]
public sealed class FeaturePoolElement : StructurePoolElement
{
    private static readonly IBlock jigsaw = BlockStateProperties.GetState("minecraft:jigsaw",
        new Dictionary<string, string> { ["orientation"] = "down_south" });

    private static readonly NbtCompound jigsawData = new()
    {
        new NbtTag<string>("name", "minecraft:bottom"),
        new NbtTag<string>("final_state", "minecraft:air"),
        new NbtTag<string>("pool", StructureTemplatePool.EmptyId),
        new NbtTag<string>("target", JigsawBlockInfo.EmptyId),
        new NbtTag<string>("joint", "rollable")
    };

    public required PlacedFeature Feature { get; init; }

    /// <summary>
    /// The JSON the placed features defined inline were read from, which a feature element saves in place of an
    /// identifier.
    /// </summary>
    internal static ConditionalWeakTable<PlacedFeature, string> InlineDefinitions { get; } = [];

    /// <summary>
    /// Whether the jigsaw has no name, like 26.3's, which attaches to any target; 1.21.11's is
    /// <c>minecraft:bottom</c>.
    /// </summary>
    internal bool UnnamedJigsaw { get; set; }

    // A feature defined inline is saved as its definition, which vanilla's codec reads like its own.
    private protected override void SaveFields(NbtCompound tag)
    {
        if (InlineDefinitions.TryGetValue(this.Feature, out var definition))
        {
            var feature = JsonNbt.ToCompound(definition);
            feature.Name = "feature";
            tag.Add(feature);
        }
        else
        {
            tag.Add(new NbtTag<string>("feature", this.Feature.Identifier));
        }

        base.SaveFields(tag);
    }

    public override Vector GetSize(StructureRotation rotation) => Vector.Zero;

    public override List<JigsawBlockInfo> GetShuffledJigsawBlocks(
        Vector position,
        StructureRotation rotation,
        IRandomSource random)
    {
        var info = JigsawBlockInfo.Of(new StructureBlockInfo(position, jigsaw, jigsawData));

        return [this.UnnamedJigsaw ? info with { Name = null } : info];
    }

    public override BlockBox GetBoundingBox(Vector position, StructureRotation rotation) => new(position, position);

    public override bool Place(StructurePieceContext context, Vector position, Vector pivot, StructureRotation rotation, BlockBox box,
        IRandomSource random, LiquidSettings liquidSettings, bool keepJigsaws) =>
        this.Feature.Place(context.Level, context.Generation, random, position);
}

/// <summary>
/// Places nothing and ends the branch, like vanilla's <c>EmptyPoolElement</c>.
/// </summary>
[StructureType("minecraft:empty_pool_element")]
public sealed class EmptyPoolElement : StructurePoolElement
{
    public static EmptyPoolElement Instance { get; } = new() { Type = "minecraft:empty_pool_element" };

    public EmptyPoolElement() => this.SetProjection(Projection.TerrainMatching);

    // Vanilla's empty element has no fields.
    private protected override void SaveFields(NbtCompound tag)
    {
    }

    public override Vector GetSize(StructureRotation rotation) => Vector.Zero;

    public override List<JigsawBlockInfo> GetShuffledJigsawBlocks(Vector position, StructureRotation rotation, IRandomSource random) => [];

    public override BlockBox GetBoundingBox(Vector position, StructureRotation rotation) =>
        throw new InvalidOperationException("Empty pool elements have no bounding box.");

    public override bool Place(StructurePieceContext context, Vector position, Vector pivot, StructureRotation rotation, BlockBox box,
        IRandomSource random, LiquidSettings liquidSettings, bool keepJigsaws) => true;
}
