using Obsidian.API.Effects;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Loot.Numbers;
using Obsidian.API.Registries;

namespace Obsidian.API.Loot.Functions;

/// <summary>
/// Adds one of <see cref="Effects"/>, picked at random, to a suspicious stew. Other items are left alone.
/// </summary>
[LootType("minecraft:set_stew_effect")]
public sealed class SetStewEffectFunction : LootFunction
{
    /// <summary>
    /// Gets the effects to pick from, each equally likely (vanilla's <c>effects</c>); the function does nothing when
    /// empty.
    /// </summary>
    public ImmutableArray<StewEffect> Effects { get; init; } = [];

    /// <inheritdoc/>
    protected override ItemStack Run(ItemStack stack, LootContext context)
    {
        if (!LootItems.Is(stack, ItemsRegistry.SuspiciousStew) || this.Effects.Length == 0)
            return stack;

        var effect = this.Effects[context.Random.NextInt(this.Effects.Length)];
        var duration = effect.Duration.GetInt(context);

        // Durations are in seconds, except for instant effects, which vanilla doesn't convert to ticks.
        if (effect.Type is not (MobEffect.InstantHealth or MobEffect.InstantDamage or MobEffect.Saturation))
            duration *= 20;

        var current = stack.GetComponent<SimpleDataComponent<ImmutableArray<SuspiciousStewEffect>>>(
            DataComponentType.SuspiciousStewEffects)?.Value ?? [];
        var added = new SuspiciousStewEffect { EffectId = (int)effect.Type, Duration = duration };

        LootItems.Set(stack, ComponentBuilder.SuspiciousStewEffects with { Value = [.. current, added] });
        return stack;
    }
}

/// <summary>
/// An effect a suspicious stew can get, with its duration in seconds.
/// </summary>
public sealed class StewEffect
{
    /// <summary>
    /// Gets the effect the stew gives (vanilla's <c>type</c>).
    /// </summary>
    public required MobEffect Type { get; init; }

    /// <summary>
    /// Gets the effect's duration in seconds, converted to ticks except for instant effects (vanilla's
    /// <c>duration</c>).
    /// </summary>
    public required INumberProvider Duration { get; init; }
}
