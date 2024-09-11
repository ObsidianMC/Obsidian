namespace Obsidian.API.ItemComponents;
public interface IItemComponent
{
    public ItemComponentType Type { get; }
    public string Identifier { get; }

    public void Write(INetStreamWriter writer);
    public void Read(INetStreamReader reader);
}

public enum ItemComponentType : int
{
    CustomData,
    MaxStackSize,
    MaxDamage,
    Damage,
    Unbreakable,
    CustomName,
    ItemName,
    Lore,
    Rarity,
    Enchantments,
    CanPlaceOn,
    CanBreak,
    AttributeModifier,
    CustomModelData,
    HideAdditionalTooltip,
    HideTooltip,
    RepairCost,
    CreativeSlotLock,
    EnchantmentGlintOverride,
    IntangibleProjectile,
    Food,
    FireResistant,
    Tool,
    StoredEnchantments,
    DyedColor,
    MapColor,
    MapId,
    MapDecorations,
    MapPostProcessing,
    ChargedProjectiles,
    ByndleContents,
    PotionContents,
    SuspiciousStewEffects,
    WritableBookContent,
    WrittenBookContent,
    Trim,
    DebugStickState,
    EntityData,
    BucketEntityData,
    BlockEntityData,
    Instruments,
    OminousBottleAmplifier,
    Recipes,
    LodestoneTracker,
    FireworkExplosion,
    Fireworks,
    Profile,
    NoteBlockSound,
    BannerPatterns,
    BaseColor,
    PotDecorations,
    Container,
    BLockState,
    Bees,
    Lock,
    ContainerLoot
}
