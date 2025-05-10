using Obsidian.API.Utilities;

namespace Obsidian.WorldData.Features.BlocksPredicates;
public sealed class AllOrAnyPredicate : IBlockPredicate
{
    /// <remarks>
    /// Valid types are minecraft:all_of or minecraft:any_of
    /// </remarks>
    public required string Type { get; init; }

    public List<IBlockPredicate> Predicates { get; } = [];

    public bool GetResult(BlockPredicateContext context)
    {
        var type = this.Type.TrimResourceTag(true);

        return type == "all_of" ? this.Predicates.All(x => x.GetResult(context)) : this.Predicates.Any(x => x.GetResult(context));
    }

}
