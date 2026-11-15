namespace Obsidian.API.Inventory.DataComponents;

public static partial class ComponentBuilder
{
    // Key by generated registry enum, never by position in a hand-maintained array.
    /// <summary>
    /// Creates an empty component of <paramref name="type"/> for a reader to fill, as the server does when it decodes
    /// components; null for a type without one.
    /// </summary>
    /// <remarks>
    /// Most types return the matching property's template, but some return a bare instance instead: the
    /// <c>minecraft:tool</c>, <c>minecraft:consumable</c> and <c>minecraft:equippable</c> ones don't have the defaults
    /// that <see cref="Tool"/>, <see cref="Consumable"/> and <see cref="Equippable"/> set. Build new components from
    /// the properties.
    /// </remarks>
    public static DataComponent? Create(DataComponentType type) => type switch
    {
        DataComponentType.MaxStackSize => MaxStackSize,
        DataComponentType.MaxDamage => MaxDamage,
        DataComponentType.Damage => Damage,
        DataComponentType.Unbreakable => Unbreakable,
        DataComponentType.CustomName => CustomName,
        DataComponentType.ItemName => ItemName,
        DataComponentType.ItemModel => ItemModel,
        DataComponentType.Lore => Lore,
        DataComponentType.Rarity => Rarity,
        DataComponentType.Enchantments => Enchantments,
        DataComponentType.RepairCost => RepairCost,
        DataComponentType.CreativeSlotLock => CreativeSlotLock,
        DataComponentType.EnchantmentGlintOverride => EnchantmentGlintOverride,
        DataComponentType.Food => new FoodDataComponent(),
        DataComponentType.UseRemainder => UseRemainder,
        DataComponentType.UseCooldown => new UseCooldownDataComponent(),
        DataComponentType.Tool => new ToolDataComponent(),
        DataComponentType.Enchantable => Enchantable,
        DataComponentType.Glider => Glider,
        DataComponentType.TooltipStyle => TooltipStyle,
        DataComponentType.StoredEnchantments => StoredEnchantments,
        DataComponentType.DyedColor => DyedColor,
        DataComponentType.MapId => MapId,
        DataComponentType.MapPostProcessing => MapPostProcessing,
        DataComponentType.ChargedProjectiles => ChargedProjectiles,
        DataComponentType.BundleContents => BundleContents,
        DataComponentType.PotionContents => new PotionContentsDataComponent(),
        DataComponentType.SuspiciousStewEffects => SuspiciousStewEffects,
        DataComponentType.WritableBookContent => WritableBookContent,
        DataComponentType.Instrument => Instrument,
        DataComponentType.OminousBottleAmplifier => OminousBottleAmplifier,
        DataComponentType.LodestoneTracker => new LodestoneTrackerDataComponent(),
        DataComponentType.FireworkExplosion => FireworkExplosion,
        DataComponentType.Fireworks => new FireworksDataComponent(),
        DataComponentType.NoteBlockSound => NoteBlockSound,
        DataComponentType.BaseColor => BaseColor,
        DataComponentType.PotDecorations => PotDecorations,
        DataComponentType.Container => Container,
        DataComponentType.BlockState => BlockState,
        DataComponentType.Consumable => new ConsumableDataComponent(),
        DataComponentType.Equippable => new EquippableDataComponent(),
        DataComponentType.Profile => new ProfileDataComponent(),
        DataComponentType.Trim => new TrimDataComponent(),
        DataComponentType.MapDecorations => new MapDecorationDataComponent(),
        DataComponentType.CustomModelData => new CustomModelDataComponent(),
        DataComponentType.WrittenBookContent => new WrittenBookDataComponent(),
        DataComponentType.DeathProtection => DeathProtection,
        DataComponentType.Repairable => Repairable,
        DataComponentType.DamageResistant => DamageResistant,
        DataComponentType.AttributeModifiers => AttributeModifiers,
        DataComponentType.BannerPatterns => BannerPatterns,
        DataComponentType.JukeboxPlayable => new JukeboxPlayableDataComponent(),
        DataComponentType.CustomData => CustomData,
        DataComponentType.IntangibleProjectile => IntangibleProjectile,
        DataComponentType.DebugStickState => DebugStickState,
        DataComponentType.BucketEntityData => BucketEntityData,
        DataComponentType.Lock => Lock,
        DataComponentType.ContainerLoot => ContainerLoot,
        DataComponentType.CanPlaceOn => new CanPlaceOnDataComponent(),
        DataComponentType.CanBreak => new CanBreakDataComponent(),
        DataComponentType.EntityData => EntityData,
        DataComponentType.BlockEntityData => BlockEntityData,
        DataComponentType.Bees => Bees,
        DataComponentType.Recipes => Recipes,
        DataComponentType.AdditionalTradeCost => AdditionalTradeCost,
        DataComponentType.VillagerFood => VillagerFood,
        DataComponentType.Dye => Dye,
        DataComponentType.CushionColor => CushionColor,
        DataComponentType.Waxed => Waxed,
        DataComponentType.Compostable => Compostable,
        DataComponentType.MobVisibility => MobVisibility,
        DataComponentType.CookingFuel => CookingFuel,
        DataComponentType.BrewingFuel => BrewingFuel,
        DataComponentType.SulfurCubeContent => SulfurCubeContent,
        DataComponentType.BlockTransformer => BlockTransformer,
        DataComponentType.ProvidesPotteryPattern => ProvidesPotteryPattern,
        _ => null
    };
}
