using Obsidian.API.World.Features;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Features;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features.Trees;

/// <summary>
/// 26.3's <c>minecraft:fallen_tree</c>: a one-block stump, and a log lying two or three blocks away on solid ground
/// (with gaps of at most two) when it fits whole. Both are decorated, the log by its blocks' Java hash set order
/// sorted by Y. Each log marks the blocks above it for post-processing, so a plant left standing on it breaks. Vanilla
/// always reports a placement, even when only the stump fit.
/// </summary>
internal sealed class FallenTreeFeature
{
    private readonly TreeTags tags;
    private readonly StateProvider trunkProvider;
    private readonly IntProvider logLength;
    private readonly TreeDecorator[] stumpDecorators;
    private readonly TreeDecorator[] logDecorators;

    public FallenTreeFeature(FeatureRegistry registry, JsonElement json)
    {
        this.tags = new TreeTags(registry);
        this.trunkProvider = registry.Provider(json.GetProperty("trunk_provider"));
        this.logLength = ValueProviders.Int(json.GetProperty("log_length"));
        this.stumpDecorators = Decorators(registry, json, "stump_decorators");
        this.logDecorators = Decorators(registry, json, "log_decorators");
    }

    public bool Place(DecorationContext context, IRandomSource random, Vector origin)
    {
        var level = context.Level;

        var stump = this.PlaceLog(level, random, origin, sideways: null);
        this.Decorate(context, random, [stump], this.stumpDecorators);

        var direction = FeatureHelpers.RandomHorizontal(random);
        var length = this.logLength(random) - 2;
        var distance = 2 + random.NextInt(2);
        var start = this.FindGround(level, origin.Offset(direction, distance));

        if (!this.CanPlaceEntireLog(level, length, start, direction))
        {
            return true;
        }

        var logs = new List<Vector>(length);
        var position = start;

        for (var i = 0; i < length; i++)
        {
            logs.Add(this.PlaceLog(level, random, position, direction));
            position = position.Offset(direction);
        }

        var hashOrder = FeatureHelpers.JavaHashSetOrder(logs);
        this.Decorate(context, random, hashOrder, this.logDecorators);
        return true;
    }

    private static TreeDecorator[] Decorators(FeatureRegistry registry, JsonElement json, string name) =>
        json.GetProperty(name).EnumerateArray().Select(entry => TreeDecorator.Read(registry, entry)).ToArray();

    // From one block above, down six blocks to the first valid tree position over solid ground.
    private Vector FindGround(IWorldGenLevel level, Vector position)
    {
        position += Vector.Up;

        for (var i = 0; i < 6; i++)
        {
            if (this.tags.ValidTreePos(level, position) && IsOverSolidGround(level, position))
            {
                return position;
            }

            position += Vector.Down;
        }

        return position;
    }

    private bool CanPlaceEntireLog(IWorldGenLevel level, int length, Vector position, BlockFace direction)
    {
        var gap = 0;

        for (var i = 0; i < length; i++)
        {
            if (!this.tags.ValidTreePos(level, position))
            {
                return false;
            }

            if (IsOverSolidGround(level, position))
            {
                gap = 0;
            }
            else
            {
                gap++;

                if (gap > 2)
                {
                    return false;
                }
            }

            position = position.Offset(direction);
        }

        return true;
    }

    private Vector PlaceLog(IWorldGenLevel level, IRandomSource random, Vector position, BlockFace? sideways)
    {
        var state = FeatureRegistry.State(this.trunkProvider, level, random, position);

        if (sideways is not null)
        {
            var axis = sideways.Value is BlockFace.East or BlockFace.West ? "x" : "z";
            state = state.WithProperty("axis", axis);
        }

        level.SetBlock(position, state);
        FeatureHelpers.MarkAboveForPostProcessing(level, position);
        return position;
    }

    private void Decorate(
        DecorationContext context, IRandomSource random, List<Vector> logs, TreeDecorator[] decorators)
    {
        if (decorators.Length == 0)
        {
            return;
        }

        var sorted = logs.OrderBy(log => log.Y).ToArray();
        Action<Vector, IBlock> setter = (position, block) => context.Level.SetBlock(position, block);
        var decoration = new TreeDecoration(context, random, this.tags, sorted, [], [], setter);

        foreach (var decorator in decorators)
        {
            decorator.Place(decoration);
        }
    }

    private static bool IsOverSolidGround(IWorldGenLevel level, Vector position) =>
        level.GetBlock(position + Vector.Down).IsFaceSturdy(BlockFace.Up);
}
