namespace Obsidian.API.Inventory;

/// <summary>
/// Vanilla's enchantments by name, in an older order: the values aren't the registry ids <see cref="Enchantment.Id"/>
/// holds, and newer enchantments are missing.
/// </summary>
public enum EnchantmentType : int
{
    /// <summary>
    /// The <c>minecraft:protection</c> enchantment.
    /// </summary>
    Protection,
    /// <summary>
    /// The <c>minecraft:fire_protection</c> enchantment.
    /// </summary>
    FireProtection,
    /// <summary>
    /// The <c>minecraft:feather_falling</c> enchantment.
    /// </summary>
    FeatherFalling,
    /// <summary>
    /// The <c>minecraft:blast_protection</c> enchantment.
    /// </summary>
    BlastProtection,
    /// <summary>
    /// The <c>minecraft:projectile_protection</c> enchantment.
    /// </summary>
    ProjectileProtection,
    /// <summary>
    /// The <c>minecraft:respiration</c> enchantment.
    /// </summary>
    Respiration,
    /// <summary>
    /// The <c>minecraft:aqua_affinity</c> enchantment.
    /// </summary>
    AquaAffinity,
    /// <summary>
    /// The <c>minecraft:thorns</c> enchantment.
    /// </summary>
    Thorns,
    /// <summary>
    /// The <c>minecraft:depth_strider</c> enchantment.
    /// </summary>
    DepthStrider,
    /// <summary>
    /// The <c>minecraft:frost_walker</c> enchantment.
    /// </summary>
    FrostWalker,
    /// <summary>
    /// The <c>minecraft:binding_curse</c> enchantment.
    /// </summary>
    BindingCurse,
    /// <summary>
    /// The <c>minecraft:soul_speed</c> enchantment.
    /// </summary>
    SoulSpeed,
    /// <summary>
    /// The <c>minecraft:sharpness</c> enchantment.
    /// </summary>
    Sharpness,
    /// <summary>
    /// The <c>minecraft:smite</c> enchantment.
    /// </summary>
    Smite,
    /// <summary>
    /// The <c>minecraft:bane_of_arthropods</c> enchantment.
    /// </summary>
    BaneOfArthropods,
    /// <summary>
    /// The <c>minecraft:knockback</c> enchantment.
    /// </summary>
    Knockback,
    /// <summary>
    /// The <c>minecraft:fire_aspect</c> enchantment.
    /// </summary>
    FireAspect,
    /// <summary>
    /// The <c>minecraft:looting</c> enchantment.
    /// </summary>
    Looting,
    /// <summary>
    /// The <c>minecraft:sweeping_edge</c> enchantment.
    /// </summary>
    Sweeping,
    /// <summary>
    /// The <c>minecraft:efficiency</c> enchantment.
    /// </summary>
    Efficiency,
    /// <summary>
    /// The <c>minecraft:silk_touch</c> enchantment.
    /// </summary>
    SilkTouch,
    /// <summary>
    /// The <c>minecraft:unbreaking</c> enchantment.
    /// </summary>
    Unbreaking,
    /// <summary>
    /// The <c>minecraft:fortune</c> enchantment.
    /// </summary>
    Fortune,
    /// <summary>
    /// The <c>minecraft:power</c> enchantment.
    /// </summary>
    Power,
    /// <summary>
    /// The <c>minecraft:punch</c> enchantment.
    /// </summary>
    Punch,
    /// <summary>
    /// The <c>minecraft:flame</c> enchantment.
    /// </summary>
    Flame,
    /// <summary>
    /// The <c>minecraft:infinity</c> enchantment.
    /// </summary>
    Infinity,
    /// <summary>
    /// The <c>minecraft:luck_of_the_sea</c> enchantment.
    /// </summary>
    LuckOfTheSea,
    /// <summary>
    /// The <c>minecraft:lure</c> enchantment.
    /// </summary>
    Lure,
    /// <summary>
    /// The <c>minecraft:loyalty</c> enchantment.
    /// </summary>
    Loyalty,
    /// <summary>
    /// The <c>minecraft:impaling</c> enchantment.
    /// </summary>
    Impaling,
    /// <summary>
    /// The <c>minecraft:riptide</c> enchantment.
    /// </summary>
    Riptide,
    /// <summary>
    /// The <c>minecraft:channeling</c> enchantment.
    /// </summary>
    Channeling,
    /// <summary>
    /// The <c>minecraft:multishot</c> enchantment.
    /// </summary>
    Multishot,
    /// <summary>
    /// The <c>minecraft:quick_charge</c> enchantment.
    /// </summary>
    QuickCharge,
    /// <summary>
    /// The <c>minecraft:piercing</c> enchantment.
    /// </summary>
    Piercing,
    /// <summary>
    /// The <c>minecraft:mending</c> enchantment.
    /// </summary>
    Mending,
    /// <summary>
    /// The <c>minecraft:vanishing_curse</c> enchantment.
    /// </summary>
    VanishingCurse,
}
