using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Features.Tree;
using Obsidian.API.World.Features;
using System.IO;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features.Trees;

/// <summary>Adds blocks around a placed tree, like vanilla's <c>TreeDecorator</c>.</summary>
internal abstract class TreeDecorator
{
    public abstract void Place(TreeDecoration context);

    public static TreeDecorator Read(FeatureRegistry registry, JsonElement json)
    {
        var type = json.GetProperty("type").GetString();

        return type switch
        {
            "minecraft:place_on_ground" => new PlaceOnGroundDecorator(registry, json),
            "minecraft:beehive" => new BeehiveDecorator(json),
            "minecraft:shelf_mushroom" => new ShelfMushroomDecorator(json),
            "minecraft:leave_vine" => new LeaveVineDecorator(json),
            "minecraft:trunk_vine" => new TrunkVineDecorator(),
            "minecraft:attached_to_leaves" => new AttachedToLeavesDecorator(registry, json),
            "minecraft:attached_to_logs" => new AttachedToLogsDecorator(registry, json),
            "minecraft:alter_ground" => new AlterGroundDecorator(registry, json),
            "minecraft:pale_moss" => new PaleMossDecorator(json),
            "minecraft:cocoa" => new CocoaDecorator(json),
            "minecraft:creaking_heart" => new CreakingHeartDecorator(json),
            _ => throw new InvalidDataException($"Unknown tree decorator {type}.")
        };
    }

    /// <summary>Directions of a holder set's list: one name or a list of names.</summary>
    protected static BlockFace[] Faces(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.String)
        {
            return [FeatureHelpers.ParseFace(json.GetString())];
        }

        return json.EnumerateArray().Select(entry => FeatureHelpers.ParseFace(entry.GetString())).ToArray();
    }
}

/// <summary>
/// What decorators see of a placed tree or fallen log: its logs, leaves and roots each sorted by Y (stably, like
/// vanilla's <c>ObjectArrayList.sort</c> over the sets' iteration order), and a setter for decorations.
/// </summary>
internal sealed class TreeDecoration(
    DecorationContext context,
    IRandomSource random,
    TreeTags tags,
    IReadOnlyList<Vector> logs,
    IReadOnlyList<Vector> leaves,
    IReadOnlyList<Vector> roots,
    Action<Vector, IBlock> setter)
{
    public DecorationContext Context { get; } = context;

    public IWorldGenLevel Level => this.Context.Level;

    public IRandomSource Random { get; } = random;

    public TreeTags Tags { get; } = tags;

    public IReadOnlyList<Vector> Logs { get; } = logs;

    public IReadOnlyList<Vector> Leaves { get; } = leaves;

    public IReadOnlyList<Vector> Roots { get; } = roots;

    /// <summary>A standing tree's view: decorators only add decorations, so its other sets stay as sorted.</summary>
    public static TreeDecoration ForTree(TreePlacement tree)
    {
        var logs = SortedByY(tree.Logs);
        var leaves = SortedByY(tree.Foliage);
        var roots = SortedByY(tree.Roots);

        return new TreeDecoration(tree.Context, tree.Random, tree.Tags, logs, leaves, roots, tree.SetDecoration);
    }

    public void SetBlock(Vector position, IBlock block) => setter(position, block);

    public bool IsAir(Vector position) => this.Level.GetBlock(position).IsAir;

    public void PlaceVine(Vector position, BlockFace face)
    {
        var vine = BlocksRegistry.Get("minecraft:vine").WithProperty(FeatureHelpers.FaceName(face), true);

        this.SetBlock(position, vine);
    }

    /// <summary>Vanilla <c>getLowestTrunkOrRootOfTree</c>.</summary>
    public IReadOnlyList<Vector> LowestTrunkOrRoot()
    {
        if (this.Roots.Count == 0)
        {
            return this.Logs;
        }

        if (this.Logs.Count > 0 && this.Roots[0].Y == this.Logs[0].Y)
        {
            return [.. this.Logs, .. this.Roots];
        }

        return this.Roots;
    }

    /// <summary>Vanilla's <c>Util.shuffle</c>.</summary>
    public static void Shuffle<T>(IList<T> list, IRandomSource random)
    {
        for (var i = list.Count; i > 1; i--)
        {
            var j = random.NextInt(i);
            (list[i - 1], list[j]) = (list[j], list[i - 1]);
        }
    }

    // A stable sort by Y: the keys put the set's iteration order after Y.
    private static Vector[] SortedByY(VanillaBlockPosSet positions)
    {
        var sorted = new Vector[positions.Count];
        var keys = new long[sorted.Length];
        var index = 0;

        foreach (var position in positions)
        {
            keys[index] = (long)position.Y << 32 | (uint)index;
            sorted[index] = position;
            index++;
        }

        Array.Sort(keys, sorted);
        return sorted;
    }
}
