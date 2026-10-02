using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.Providers.BlockStateProviders;

/// <summary>
/// The first rule whose predicate matches decides the state; otherwise the fallback does. Used by disks and the like.
/// Like vanilla's RuleBasedBlockStateProvider, this isn't a typed provider: it's written as <c>{"fallback", "rules"}</c>.
/// </summary>
public sealed class RuleBasedStateProvider
{
    public required IBlockStateProvider Fallback { get; init; }

    public ImmutableArray<Rule> Rules { get; init; } = [];

    public IBlock GetState(IWorldGenLevel level, IRandomSource random, Vector position)
    {
        foreach (var rule in this.Rules)
        {
            if (rule.IfTrue.Test(level, position))
                return rule.Then.GetState(random, position);
        }

        return this.Fallback.GetState(random, position);
    }

    public sealed class Rule
    {
        public required IBlockPredicate IfTrue { get; init; }

        public required IBlockStateProvider Then { get; init; }
    }
}
