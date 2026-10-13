using Obsidian.API.Loot.Predicates;

namespace Obsidian.API.Loot.Conditions;

/// <summary>
/// Passes when the block survives the explosion: always without an explosion, otherwise with a <c>1 / radius</c> chance.
/// </summary>
[LootType("minecraft:survives_explosion")]
public sealed class SurvivesExplosionCondition : ILootCondition
{
    public bool Test(LootContext context)
    {
        if (context.ExplosionRadius is not { } radius)
            return true;

        return context.Random.NextFloat() <= 1f / radius;
    }
}

/// <summary>
/// Passes when the block is <see cref="Block"/> with the given vanilla state property values.
/// </summary>
[LootType("minecraft:block_state_property")]
public sealed class BlockStatePropertyCondition : ILootCondition
{
    public required string Block { get; init; }

    public required Dictionary<string, string> Properties { get; init; }

    public bool Test(LootContext context)
    {
        if (context.Block is not { } block || block.UnlocalizedName != this.Block)
            return false;

        var property = context.BlockProperty ?? throw new InvalidOperationException("The loot context has no block property reader.");
        foreach (var (name, value) in this.Properties)
        {
            // A missing property fails the condition; it never matches a default.
            if (property(block, name) != value)
                return false;
        }

        return true;
    }
}

/// <summary>
/// Passes when the tool the block was broken with matches <see cref="Predicate"/>.
/// </summary>
[LootType("minecraft:match_tool")]
public sealed class MatchToolCondition : ILootCondition
{
    public required ToolPredicate Predicate { get; init; }

    public bool Test(LootContext context) => context.Tool is { } tool && this.Predicate.Matches(tool, context);
}

/// <summary>
/// Passes when any term passes; stops at the first that does.
/// </summary>
[LootType("minecraft:any_of")]
public sealed class AnyOfCondition : ILootCondition
{
    public required ImmutableArray<ILootCondition> Terms { get; init; }

    public bool Test(LootContext context)
    {
        foreach (var term in this.Terms)
        {
            if (term.Test(context))
                return true;
        }

        return false;
    }
}

/// <summary>
/// Negates <see cref="Term"/>.
/// </summary>
[LootType("minecraft:inverted")]
public sealed class InvertedCondition : ILootCondition
{
    public required ILootCondition Term { get; init; }

    public bool Test(LootContext context) => !this.Term.Test(context);
}

/// <summary>
/// Passes with the chance for the tool's <see cref="Enchantment"/> level, which indexes <see cref="Chances"/> (clamped to the
/// last entry). Bare hands and absent tools have level 0. The random draw happens even at level 0, like vanilla.
/// </summary>
[LootType("minecraft:table_bonus")]
public sealed class TableBonusCondition : ILootCondition
{
    public required EnchantmentDefinition Enchantment { get; init; }

    public required ImmutableArray<float> Chances { get; init; }

    public bool Test(LootContext context)
    {
        var level = EnchantmentHelper.GetLevel(context.Tool, this.Enchantment);
        var chance = this.Chances[Math.Min(level, this.Chances.Length - 1)];
        return context.Random.NextFloat() < chance;
    }
}

/// <summary>
/// Tests the block at <see cref="OffsetY"/> above the loot origin's block (or below for negative values).
/// </summary>
[LootType("minecraft:location_check")]
public sealed class LocationCheckCondition : ILootCondition
{
    public int OffsetY { get; init; }

    public required LocationPredicate Predicate { get; init; }

    public bool Test(LootContext context)
    {
        var origin = context.Origin ?? throw new InvalidOperationException("location_check needs a loot origin.");
        var blockAt = context.BlockAt ?? throw new InvalidOperationException("The loot context has no block reader.");

        var block = blockAt((int)Math.Floor(origin.X), (int)Math.Floor(origin.Y) + this.OffsetY, (int)Math.Floor(origin.Z));
        if (block is null)
            return false;

        return this.Predicate.Block?.Matches(block, context) ?? true;
    }
}

/// <summary>
/// Tests an entity. Vanilla block tables only check <c>this</c> against an empty predicate, so it passes when the breaker
/// (<see cref="LootContext.ThisEntity"/>) is present.
/// </summary>
[LootType("minecraft:entity_properties")]
public sealed class EntityPropertiesCondition : ILootCondition
{
    public required string Entity { get; init; }

    public required EntityPredicate Predicate { get; init; }

    public bool Test(LootContext context) => this.Entity switch
    {
        "this" => context.ThisEntity is not null,
        _ => throw LootPending.Evaluation($"minecraft:entity_properties for '{this.Entity}'"),
    };
}
