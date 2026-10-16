using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Loot.Numbers;
using Obsidian.API.Loot.Predicates;

namespace Obsidian.API.Loot.Functions;

/// <summary>
/// Keeps each item of the stack with a <c>1 / radius</c> chance, like vanilla's <c>ApplyExplosionDecay</c>. Without an
/// explosion the stack is unchanged.
/// </summary>
[LootType("minecraft:explosion_decay")]
public sealed class ExplosionDecayFunction : LootFunction
{
    protected override ItemStack Run(ItemStack stack, LootContext context)
    {
        if (context.ExplosionRadius is not { } radius)
            return stack;

        var chance = 1f / radius;
        var survivors = 0;
        for (var i = 0; i < stack.Count; i++)
        {
            if (context.Random.NextFloat() <= chance)
                survivors++;
        }

        return new ItemStack(stack, survivors);
    }
}

/// <summary>
/// Copies the listed components from the source (<c>block_entity</c>) onto the stack.
/// </summary>
[LootType("minecraft:copy_components")]
public sealed class CopyComponentsFunction : LootFunction
{
    public required ImmutableArray<string> Include { get; init; }

    public required string Source { get; init; }

    protected override ItemStack Run(ItemStack stack, LootContext context)
    {
        if (this.Source != "block_entity")
            throw new NotSupportedException($"copy_components from source '{this.Source}' isn't supported.");

        // No block entity, nothing to copy: vanilla leaves the stack as it is.
        if (context.BlockEntityComponent is not { } component)
            return stack;

        foreach (var id in this.Include)
        {
            if (component(id) is { } value)
                stack[value.Type] = value;
        }

        return stack;
    }
}

/// <summary>
/// Copies the listed block state properties into the stack's <c>minecraft:block_state</c> component.
/// </summary>
[LootType("minecraft:copy_state")]
public sealed class CopyStateFunction : LootFunction
{
    public required string Block { get; init; }

    public required ImmutableArray<string> Properties { get; init; }

    protected override ItemStack Run(ItemStack stack, LootContext context)
    {
        // Only the listed block copies its state; another block leaves the stack as it is, like vanilla.
        if (context.Block is not { } block || block.UnlocalizedName != this.Block)
            return stack;

        if (context.BlockProperty is null)
            throw new InvalidOperationException("copy_state needs LootContext.BlockProperty to read the block state.");

        var values = new List<BlockStateProperty>();
        foreach (var name in this.Properties)
        {
            if (context.BlockProperty(block, name) is { } value)
                values.Add(new BlockStateProperty { Name = name, Value = value });
        }

        if (values.Count > 0)
            stack[DataComponentType.BlockState] = ComponentBuilder.BlockState with { Value = values.ToArray() };

        return stack;
    }
}

/// <summary>
/// Raises the stack size with the tool's enchantment level, using one of vanilla's named formulas. The formula only runs
/// when the context has a tool, bare hands included.
/// </summary>
[LootType("minecraft:apply_bonus")]
public sealed class ApplyBonusFunction : LootFunction
{
    public required EnchantmentDefinition Enchantment { get; init; }

    public required string Formula { get; init; }

    public ApplyBonusParameters Parameters { get; init; } = new();

    protected override ItemStack Run(ItemStack stack, LootContext context)
    {
        if (context.Tool is null)
            return stack;

        var level = EnchantmentHelper.GetLevel(context.Tool, this.Enchantment);
        var count = stack.Count;
        var random = context.Random;
        var newCount = this.Formula switch
        {
            // ore_drops: no bonus without the enchantment; otherwise multiply by 1 + max(0, nextInt(level + 2) - 1).
            "minecraft:ore_drops" => level > 0
                ? count * (Math.Max(random.NextInt(level + 2) - 1, 0) + 1)
                : count,

            // binomial_with_bonus_count: add the successes of (level + extra) trials with the given probability.
            "minecraft:binomial_with_bonus_count" => count + CountSuccesses(random, level + this.Parameters.Extra, this.Parameters.Probability),

            // uniform_bonus_count: add a uniform amount up to bonusMultiplier per level.
            "minecraft:uniform_bonus_count" => count + random.NextInt(this.Parameters.BonusMultiplier * level + 1),

            _ => throw new NotSupportedException($"apply_bonus formula {this.Formula} isn't supported."),
        };

        return new ItemStack(stack, newCount);
    }

    private static int CountSuccesses(Obsidian.API.World.Generator.RandomSources.IRandomSource random, int trials, float probability)
    {
        var successes = 0;
        for (var i = 0; i < trials; i++)
        {
            if (random.NextFloat() < probability)
                successes++;
        }

        return successes;
    }
}

/// <summary>
/// The parameters of <see cref="ApplyBonusFunction"/>'s formulas; the ones a formula doesn't use stay at their defaults.
/// </summary>
public sealed class ApplyBonusParameters
{
    /// <summary>
    /// Extra trials of <c>binomial_with_bonus_count</c>, added to the enchantment level.
    /// </summary>
    public int Extra { get; init; }

    /// <summary>
    /// Success probability of <c>binomial_with_bonus_count</c>.
    /// </summary>
    public float Probability { get; init; }

    /// <summary>
    /// Items added per enchantment level by <c>uniform_bonus_count</c>.
    /// </summary>
    public int BonusMultiplier { get; init; }
}

/// <summary>
/// Clamps the stack size to <see cref="Limit"/>, like vanilla's <c>LimitCount</c>.
/// </summary>
[LootType("minecraft:limit_count")]
public sealed class LimitCountFunction : LootFunction
{
    public required NumberRange Limit { get; init; }

    protected override ItemStack Run(ItemStack stack, LootContext context)
    {
        var count = stack.Count;
        var min = this.Limit.Min;
        var max = this.Limit.Max;
        if (min is not null && max is not null)
        {
            var low = min.GetInt(context);
            var high = max.GetInt(context);
            count = count < low ? low : count > high ? high : count;
        }
        else if (min is not null)
        {
            count = Math.Max(min.GetInt(context), count);
        }
        else if (max is not null)
        {
            count = Math.Min(max.GetInt(context), count);
        }

        return new ItemStack(stack, count);
    }
}
