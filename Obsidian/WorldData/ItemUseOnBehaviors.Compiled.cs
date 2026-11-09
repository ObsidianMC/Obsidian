using Obsidian.API.Blocks;
using System.Collections.Frozen;
using System.Text.Json;

namespace Obsidian.WorldData;

// 26.3's block transformers compiled once: block ids in sets, target states read, faces parsed. Only the provider and
// predicate types vanilla's transformers use are supported.
internal static partial class ItemUseOnBehaviors
{
    /// <summary>A transformer's transform: the faces it ignores, what it turns a block into, and its data.</summary>
    internal sealed record CompiledTransform(
        FrozenSet<BlockFace> DisallowedFaces,
        StateProvider Provider,
        JsonElement Data);

    /// <summary>A block state provider: the state a block becomes, or null when it doesn't apply.</summary>
    internal abstract class StateProvider
    {
        public abstract IBlock? Provide(Func<Vector, IBlock> read, Vector position);
    }

    /// <summary>A block predicate, read at an offset from the clicked block.</summary>
    internal abstract class BlockPredicate(Vector offset)
    {
        public bool Matches(Func<Vector, IBlock> read, Vector position) => this.MatchesAt(read, position + offset);

        protected abstract bool MatchesAt(Func<Vector, IBlock> read, Vector position);
    }

    private sealed class FixedState(IBlock state) : StateProvider
    {
        public override IBlock? Provide(Func<Vector, IBlock> read, Vector position) => state;
    }

    // minecraft:copy_properties: the source state with the clicked block's properties it shares.
    private sealed class CopiedProperties(IBlock source) : StateProvider
    {
        public override IBlock? Provide(Func<Vector, IBlock> read, Vector position) =>
            CopyProperties(source, read(position));
    }

    // minecraft:rule_based: the provider of the first rule whose predicate holds.
    private sealed class RuleBased((BlockPredicate If, StateProvider Then)[] rules) : StateProvider
    {
        public override IBlock? Provide(Func<Vector, IBlock> read, Vector position)
        {
            foreach (var (predicate, provider) in rules)
            {
                if (predicate.Matches(read, position))
                    return provider.Provide(read, position);
            }

            return null;
        }
    }

    private sealed class AllOf(Vector offset, BlockPredicate[] predicates) : BlockPredicate(offset)
    {
        protected override bool MatchesAt(Func<Vector, IBlock> read, Vector position)
        {
            foreach (var predicate in predicates)
            {
                if (!predicate.Matches(read, position))
                    return false;
            }

            return true;
        }
    }

    private sealed class MatchingBlocks(Vector offset, FrozenSet<int> blocks) : BlockPredicate(offset)
    {
        protected override bool MatchesAt(Func<Vector, IBlock> read, Vector position) =>
            blocks.Contains(read(position).RegistryId);
    }

    // Tags are read when used, so a data pack's change to them applies.
    private sealed class MatchingTag(Vector offset, string tag) : BlockPredicate(offset)
    {
        protected override bool MatchesAt(Func<Vector, IBlock> read, Vector position) =>
            TagsRegistry.TryGet("block", tag, out var entries)
                && entries.Entries.Contains(read(position).RegistryId);
    }

    private static FrozenDictionary<string, CompiledTransform[]> CompileTransformers(
        FrozenDictionary<string, JsonElement> transformers)
    {
        var compiled = new Dictionary<string, CompiledTransform[]>();
        foreach (var (identifier, transforms) in transformers)
            compiled[identifier] = [.. transforms.EnumerateArray().Select(CompileTransform)];

        return compiled.ToFrozenDictionary();
    }

    private static CompiledTransform CompileTransform(JsonElement transform)
    {
        var faces = new HashSet<BlockFace>();
        if (transform.TryGetProperty("disallowed_faces", out var disallowed))
        {
            foreach (var face in disallowed.EnumerateArray())
                faces.Add(Enum.Parse<BlockFace>(face.GetString()!, ignoreCase: true));
        }

        var provider = CompileProvider(transform.GetProperty("block_state_provider"));
        return new CompiledTransform(faces.ToFrozenSet(), provider, transform);
    }

    private static StateProvider CompileProvider(JsonElement provider)
    {
        if (!provider.TryGetProperty("type", out var type))
            return new FixedState(ReadState(provider));

        switch (type.GetString())
        {
            case "minecraft:rule_based":
                var rules = provider.GetProperty("rules").EnumerateArray().Select(CompileRule).ToArray();
                return new RuleBased(rules);

            case "minecraft:copy_properties":
                return new CopiedProperties(ReadState(provider.GetProperty("source")));

            default:
                throw new NotSupportedException($"Unsupported transformer provider {type.GetString()}.");
        }
    }

    private static (BlockPredicate If, StateProvider Then) CompileRule(JsonElement rule)
    {
        var predicate = CompilePredicate(rule.GetProperty("if_true"));
        var provider = CompileProvider(rule.GetProperty("then"));

        return (predicate, provider);
    }

    private static BlockPredicate CompilePredicate(JsonElement predicate)
    {
        var offset = Vector.Zero;
        if (predicate.TryGetProperty("offset", out var coordinates))
            offset = new Vector(coordinates[0].GetInt32(), coordinates[1].GetInt32(), coordinates[2].GetInt32());

        switch (predicate.GetProperty("type").GetString())
        {
            case "minecraft:all_of":
                var children = predicate.GetProperty("predicates").EnumerateArray().Select(CompilePredicate).ToArray();
                return new AllOf(offset, children);

            case "minecraft:matching_blocks":
                return new MatchingBlocks(offset, BlockIds(predicate.GetProperty("blocks")));

            case "minecraft:matching_block_tag":
                return new MatchingTag(offset, predicate.GetProperty("tag").GetString()!);

            default:
                throw new NotSupportedException("Unsupported transformer block predicate.");
        }
    }

    // A block identifier or a list of them, as registry ids.
    private static FrozenSet<int> BlockIds(JsonElement blocks)
    {
        if (blocks.ValueKind == JsonValueKind.String)
            return new[] { BlocksRegistry.Get(blocks.GetString()!).RegistryId }.ToFrozenSet();

        return blocks.EnumerateArray()
            .Select(block => BlocksRegistry.Get(block.GetString()!).RegistryId)
            .ToFrozenSet();
    }
}
