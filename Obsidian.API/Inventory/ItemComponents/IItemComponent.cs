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
    CustomData = 0,
    MaxStackSize = 1,
    MaxDamage = 2,
    Damage = 3,
    Unbreakable = 4,
    CustomName = 5,
    ItemName = 6,
    Lore = 7,
    Rarity = 8,
    Enchantments = 9,
    CanPlaceOn = 10,
    CanBreak = 11,
    AttributeModifiers = 12,
    CustomModelData = 13,
    HideAdditionalTooltip = 14,
    HideTooltip = 15,
    RepairCost = 16,
    CreativeSlotLock = 17,
    EnchantmentGlintOverride = 18,
    IntangibleProjectile = 19,
    Food = 20,
    FireResistant = 21,
    Tool = 22,
    StoredEnchantments = 23,
    DyedColor = 24,
    MapColor = 25,
    MapId = 26,
    MapDecorations = 27,
    MapPostProcessing = 28,
    ChargedProjectiles = 29,
    ByndleContents = 30,
    PotionContents = 31,
    SuspiciousStewEffects = 32,
    WritableBookContent = 33,
    WrittenBookContent = 34,
    Trim = 35,
    DebugStickState = 36,
    EntityData = 37,
    BucketEntityData = 38,
    BlockEntityData = 39,
    Instruments = 40,
    OminousBottleAmplifier = 41,
    JukeboxPlayable = 42,
    Recipes = 43,
    LodestoneTracker = 44,
    FireworkExplosion = 45,
    Fireworks = 46,
    Profile = 47,
    NoteBlockSound = 48,
    BannerPatterns = 49,
    BaseColor = 50,
    PotDecorations = 51,
    Container = 52,
    BLockState = 53,
    Bees = 54,
    Lock = 55,
    ContainerLoot = 56
}
