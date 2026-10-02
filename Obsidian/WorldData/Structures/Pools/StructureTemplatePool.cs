using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.Structures.Pools;

/// <summary>
/// A weighted set of pieces jigsaw blocks pick from, like vanilla's <c>StructureTemplatePool</c>. The pools vanilla
/// defines are in the generated <c>TemplatePools</c> registry.
/// </summary>
public sealed class StructureTemplatePool
{
    public const string EmptyId = "minecraft:empty";

    private int maxSize = -1;

    /// <summary>The registry id, e.g. <c>minecraft:village/plains/houses</c>.</summary>
    public string Identifier { get; init; } = string.Empty;

    /// <summary>The id of the pool used when no piece of this one fits, or at the structure's maximum depth.</summary>
    public required string Fallback { get; init; }

    public required StructurePoolEntry[] Elements { get; init; }

    /// <summary>The elements, each repeated by its weight, like vanilla's <c>templates</c> list.</summary>
    private StructurePoolElement[] Templates => field ??= [.. this.Elements.SelectMany(entry => Enumerable.Repeat(entry.Element, entry.Weight))];

    /// <summary>The number of weighted entries.</summary>
    public int Size => this.Templates.Length;

    /// <summary>
    /// Vanilla <c>getMaxSize</c>: the tallest element (by bounding box height), used by the expansion hack.
    /// </summary>
    public int MaxSize
    {
        get
        {
            // Computed once; racing threads compute the same value.
            var value = System.Threading.Volatile.Read(ref this.maxSize);
            if (value < 0)
            {
                value = this.Templates
                    .Where(element => element is not EmptyPoolElement)
                    .Select(element => element.GetBoundingBox(Vector.Zero, StructureRotation.None).YSpan)
                    .DefaultIfEmpty(0)
                    .Max();
                System.Threading.Volatile.Write(ref this.maxSize, value);
            }

            return value;
        }
    }

    /// <summary>The fallback pool, or <c>null</c> when its id isn't registered.</summary>
    public StructureTemplatePool? FallbackPool => Get(this.Fallback);

    /// <summary>Vanilla <c>getRandomTemplate</c>: one <c>nextInt</c> over the weighted entries.</summary>
    public StructurePoolElement GetRandomTemplate(IRandomSource random) =>
        this.Templates.Length == 0 ? EmptyPoolElement.Instance : this.Templates[random.NextInt(this.Templates.Length)];

    /// <summary>Vanilla <c>getShuffledTemplates</c>: the weighted entries in a random order.</summary>
    public List<StructurePoolElement> GetShuffledTemplates(IRandomSource random)
    {
        var templates = this.Templates.ToList();
        FeatureHelpers.Shuffle(templates, random);
        return templates;
    }

    /// <summary>The registered pool with the id, or <c>null</c>.</summary>
    public static StructureTemplatePool? Get(string id) => TemplatePools.All.GetValueOrDefault(id);
}

/// <summary>
/// An element of a <see cref="StructureTemplatePool"/> and its weight.
/// </summary>
public sealed class StructurePoolEntry
{
    public required StructurePoolElement Element { get; init; }

    /// <summary>How many times the element is in the pool (1 to 150).</summary>
    public required int Weight { get; init; }
}

/// <summary>
/// How a pool element sits on the terrain, like vanilla's <c>StructureTemplatePool.Projection</c>.
/// </summary>
public enum Projection
{
    /// <summary>Follows the terrain block by block (paths, fields).</summary>
    TerrainMatching,

    /// <summary>Keeps its shape; the terrain adapts around it.</summary>
    Rigid
}
