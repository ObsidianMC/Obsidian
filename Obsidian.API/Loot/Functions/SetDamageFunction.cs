using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Loot.Numbers;
using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.API.Loot.Functions;

/// <summary>
/// Sets the remaining durability of a damageable item as a fraction of its max damage (1 = undamaged), or adds to it
/// when <see cref="Add"/> is set. Other items are left alone.
/// </summary>
[LootType("minecraft:set_damage")]
public sealed class SetDamageFunction : LootFunction
{
    public required INumberProvider Damage { get; init; }

    public bool Add { get; init; }

    protected override ItemStack Run(ItemStack stack, LootContext context)
    {
        var maxDamage = stack.Holder.MaxDamage;
        if (maxDamage == 0 || stack.ContainsKey(DataComponentType.Unbreakable))
            return stack;

        var current = this.Add ? 1.0f - (float)stack.Damage / maxDamage : 0.0f;
        var durability = 1.0f - Math.Clamp(this.Damage.GetFloat(context) + current, 0.0f, 1.0f);
        var damage = Math.Clamp(Mth.Floor(durability * maxDamage), 0, maxDamage);

        LootItems.Set(stack, ComponentBuilder.Damage with { Value = damage });
        return stack;
    }
}
