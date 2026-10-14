namespace Obsidian.API.Inventory;

/// <summary>
/// The ids of attributes that items' attribute modifiers change, for <see cref="AttributeModifier.Id"/>.
/// </summary>
/// <remarks>
/// They're the attributes' protocol ids, their positions in vanilla 1.21.11's attribute registry, which clients read
/// the modifiers with. Obsidian applies armour, armour toughness and knockback resistance from worn items and attack
/// damage and speed from held ones; modifiers of other attributes are kept and sent to clients but don't change the
/// holder.
/// </remarks>
public static class AttributeIds
{
    public const int Armor = 0;

    public const int ArmorToughness = 1;

    public const int AttackDamage = 2;

    public const int AttackKnockback = 3;

    public const int AttackSpeed = 4;

    public const int EntityInteractionRange = 10;

    public const int FallDamageMultiplier = 11;

    public const int KnockbackResistance = 16;

    public const int SafeFallDistance = 24;

    public const int SweepingDamageRatio = 30;
}
