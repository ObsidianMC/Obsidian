using Obsidian.API;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Nbt;
using System.Collections.Immutable;
using Xunit;

namespace Obsidian.Tests;

public sealed partial class ItemComponentEqualityRegressions
{
    [Theory]
    [InlineData(DataComponentType.CanBreak)]
    [InlineData(DataComponentType.CanPlaceOn)]
    public void PartialPredicateOrderDoesNotPreventMerging(DataComponentType type)
    {
        var firstPartial = PartialPredicate(1, 10);
        var secondPartial = PartialPredicate(2, 20);
        var first = Predicates(type, new BlockPredicate { PartialComponents = [firstPartial, secondPartial] });
        var second = Predicates(type, new BlockPredicate { PartialComponents = [secondPartial, firstPartial] });

        AssertMerge(first, second);
    }

    [Theory]
    [InlineData(DataComponentType.CanBreak, true)]
    [InlineData(DataComponentType.CanBreak, false)]
    [InlineData(DataComponentType.CanPlaceOn, true)]
    [InlineData(DataComponentType.CanPlaceOn, false)]
    public void PartialPredicateTypeAndValueRemainSignificant(DataComponentType type, bool changeType)
    {
        var original = PartialPredicate(1, 10);
        var changed = changeType ? PartialPredicate(2, 10) : PartialPredicate(1, 20);
        var first = Predicates(type, new BlockPredicate { PartialComponents = [original] });
        var second = Predicates(type, new BlockPredicate { PartialComponents = [changed] });

        AssertDoNotMerge(first, second);
    }

    [Theory]
    [InlineData(DataComponentType.CanBreak)]
    [InlineData(DataComponentType.CanPlaceOn)]
    public void ExactPredicateOrderStillPreventsMerging(DataComponentType type)
    {
        var firstExact = ComponentBuilder.Damage with { Value = 1 };
        var secondExact = ComponentBuilder.RepairCost with { Value = 2 };
        var first = Predicates(type, new BlockPredicate { ExactComponents = [firstExact, secondExact] });
        var second = Predicates(type, new BlockPredicate { ExactComponents = [secondExact, firstExact] });

        AssertDoNotMerge(first, second);
    }

    [Theory]
    [InlineData(DataComponentType.CanBreak)]
    [InlineData(DataComponentType.CanPlaceOn)]
    public void DefaultPartialPredicatesEqualEmpty(DataComponentType type)
    {
        var first = Predicates(type, new BlockPredicate { PartialComponents = default });
        var second = Predicates(type, new BlockPredicate { PartialComponents = [] });

        AssertMerge(first, second);
    }

    private static ComponentPredicate PartialPredicate(int typeId, int value) => new()
    {
        IsPredicateType = true,
        TypeId = typeId,
        Value = new NbtCompound { new NbtTag<int>("value", value) }
    };

    private static BlockPredicatesDataComponent Predicates(DataComponentType type, BlockPredicate predicate)
    {
        ImmutableArray<BlockPredicate> predicates = [predicate];

        return type == DataComponentType.CanBreak
            ? new CanBreakDataComponent { Predicates = predicates }
            : new CanPlaceOnDataComponent { Predicates = predicates };
    }
}
