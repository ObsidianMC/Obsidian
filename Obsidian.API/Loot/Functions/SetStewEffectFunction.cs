using Obsidian.API.Effects;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Loot.Numbers;

namespace Obsidian.API.Loot.Functions;

/// <summary>
/// Adds one of <see cref="Effects"/>, picked at random, to a suspicious stew. Other items are left alone.
/// </summary>
[LootType("minecraft:set_stew_effect")]
public sealed class SetStewEffectFunction : LootFunction
{
    public ImmutableArray<StewEffect> Effects { get; init; } = [];

    protected override ItemStack Run(ItemStack stack, LootContext context)
    {
        if (!LootItems.Is(stack, Material.SuspiciousStew) || this.Effects.Length == 0)
            return stack;

        var effect = this.Effects[context.Random.NextInt(this.Effects.Length)];
        var duration = effect.Duration.GetInt(context);

        // Durations are in seconds, except for instant effects, which vanilla doesn't convert to ticks.
        if (effect.Type is not (MobEffect.InstantHealth or MobEffect.InstantDamage or MobEffect.Saturation))
            duration *= 20;

        var current = stack.GetComponent<SimpleDataComponent<SuspiciousStewEffect[]>>(DataComponentType.SuspiciousStewEffects)?.Value ?? [];
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
    public required MobEffect Type { get; init; }

    public required INumberProvider Duration { get; init; }
}
