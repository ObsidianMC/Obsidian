using Obsidian.API.Inventory;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class AwardStatsPacket
{
    [Field(0)]
    public List<Statistic> Stats { get; } = [];

    public void Add(Statistic stat)
    {
        Stats.Add(stat);
    }

    public void Clear()
    {
        Stats.Clear();
    }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(Stats.Count);
        foreach (var value in Stats)
        {
            writer.WriteVarInt(value.CategoryId);
            writer.WriteVarInt(value.StatisticId);
            writer.WriteVarInt(value.Value);
        }
    }
}

public readonly record struct Statistic
{
    public Statistic(CategoryIds category, int statisticId, int value)
    {
        this.CategoryId = category;
        this.StatisticId = statisticId;
        this.Value = value;
    }

    public CategoryIds CategoryId { get; }

    /// <summary>
    /// Can either be a block, item or entity id. For CategoryIds.Custom, use the <see cref="StatisticIds"/> enum casted to an integer.
    /// </summary>
    public int StatisticId { get; }

    public int Value { get; }

    public Statistic(CategoryIds category, IBlock block, int value)
    {
        CategoryId = category;
        StatisticId = block.BaseId;
        Value = value;
    }

    public Statistic(CategoryIds category, StatisticIds statistic, int value)
    {
        CategoryId = category;
        StatisticId = (int)statistic;
        Value = value;
    }
    public Statistic(CategoryIds category, Item item, int value)
    {
        CategoryId = category;
        StatisticId = item.Id;
        Value = value;
    }
    public Statistic(CategoryIds category, Entity entity, int value)
    {
        CategoryId = category;
        StatisticId = entity.TypeId;
        Value = value;
    }
}

public enum CategoryIds : int
{
    Mined = 0,
    Crafted = 1,
    Used = 2,
    Broken = 3,
    PickedUp = 4,
    Dropped = 5,
    Killed = 6,
    KilledBy = 7,
    Custom = 8
}

public enum StatisticIds : int
{
    LeaveGame = 0,
    PlayTime = 1,
    TotalWorldTime = 2,
    TimeSinceDeath = 3,
    TimeSinceRest = 4,
    SneakTime = 5,
    WalkOneCm = 6,
    CrouchOneCm = 7,
    SprintOneCm = 8,
    WalkOnWaterOneCm = 9,
    FallOneCm = 10,
    ClimbOneCm = 11,
    FlyOneCm = 12,
    WalkUnderWaterOneCm = 13,
    MinecartOneCm = 14,
    BoatOneCm = 15,
    PigOneCm = 16,
    HappyGhastOneCm = 17,
    HorseOneCm = 18,
    AviateOneCm = 19,
    SwimOneCm = 20,
    StriderOneCm = 21,
    NautilusOneCm = 22,
    Jump = 23,
    Drop = 24,
    DamageDealt = 25,
    DamageDealtAbsorbed = 26,
    DamageDealtResisted = 27,
    DamageTaken = 28,
    DamageBlockedByShield = 29,
    DamageAbsorbed = 30,
    DamageResisted = 31,
    Deaths = 32,
    MobKills = 33,
    AnimalsBred = 34,
    PlayerKills = 35,
    FishCaught = 36,
    TalkedToVillager = 37,
    TradedWithVillager = 38,
    EatCakeSlice = 39,
    FillCauldron = 40,
    UseCauldron = 41,
    CleanArmor = 42,
    CleanBanner = 43,
    CleanShulkerBox = 44,
    InteractWithBrewingstand = 45,
    InteractWithBeacon = 46,
    InspectDropper = 47,
    InspectHopper = 48,
    InspectDispenser = 49,
    PlayNoteblock = 50,
    TuneNoteblock = 51,
    PotFlower = 52,
    TriggerTrappedChest = 53,
    OpenEnderchest = 54,
    EnchantItem = 55,
    PlayRecord = 56,
    InteractWithFurnace = 57,
    InteractWithCraftingTable = 58,
    OpenChest = 59,
    SleepInBed = 60,
    OpenShulkerBox = 61,
    OpenBarrel = 62,
    InteractWithBlastFurnace = 63,
    InteractWithSmoker = 64,
    InteractWithLectern = 65,
    InteractWithCampfire = 66,
    InteractWithCartographyTable = 67,
    InteractWithLoom = 68,
    InteractWithStonecutter = 69,
    BellRing = 70,
    RaidTrigger = 71,
    RaidWin = 72,
    InteractWithAnvil = 73,
    InteractWithGrindstone = 74,
    TargetHit = 75,
    InteractWithSmithingTable = 76,

    // Source compatibility for the pre-1.21 names; values follow the current custom_stat registry.
    PlayOneMinute = PlayTime,
    DiveOneCm = WalkUnderWaterOneCm
}
