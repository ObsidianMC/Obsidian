using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:wandering_trader")]
public sealed partial class WanderingTrader : AgeableMob
{
    public int DespawnDelay { get; set; } = 48000;
    public Vector? WanderTarget { get; set; }
    public WanderingTrader() => Type = EntityType.WanderingTrader;
    protected override bool UsesAi => true;
    protected override bool CanDespawn => false;
    protected override string? SoundName => "wandering_trader";
    public override bool IsBaby { get => false; set { } }
    internal override ValueTask InteractAsync(IPlayer player, InteractionHand hand)
    {
        if (offers.IsEmpty) CreateOffers();
        return MerchantTrading.OpenAsync(this, player, offers, 1, 0, false);
    }
    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        ((Navigator)Navigator!).CanOpenDoors = true;
        actions.AddGoal(0, new FloatGoal(this));
        actions.AddGoal(1, new PanicGoal(this, 0.5f));
        actions.AddGoal(1, new TraderAvoidGoal(this));
        actions.AddGoal(2, new TraderWanderGoal(this));
        actions.AddGoal(7, new RandomStrollGoal(this, 1));
        actions.AddGoal(9, new LookAtPlayerGoal(this, 3));
        actions.AddGoal(10, new RandomLookAroundGoal(this));
    }
    protected override async ValueTask TickMobAsync()
    {
        await base.TickMobAsync();
        var trading = Level.GetPlayersInRange(Position, 16).Any(player => player.OpenedContainer is MerchantContainer menu && ReferenceEquals(menu.Merchant, this));
        if (!trading && DespawnDelay > 0 && --DespawnDelay == 0) { await RemoveAsync(); return; }
        if (trading) { GoalController?.Pause(); return; }
        if (GoalController is GoalSelector { IsPaused: true } goals) goals.Resume();
        if (MobBitMask.HasFlag(MobBitmask.NoAi)) return;
        var night = Level.DayTime is >= 12000 and < 24000;
        if (Invisible == night) return;
        Invisible = night;
        if (night) AddPotionEffect((int)PotionEffect.Invisibility - 1, 24000);
        else RemovePotionEffect((int)PotionEffect.Invisibility - 1);
        PlayMobSound(night ? "disappeared" : "reappeared");
        SynchronizeMetadata();
    }
    protected override void WriteAdditionalSave(INbtWriter writer)
    {
        writer.WriteInt("DespawnDelay", DespawnDelay);
        MerchantOffers.Write(writer, offers);
        if (WanderTarget is { } target)
        {
            writer.WriteCompoundStart("WanderTarget");
            writer.WriteInt("X", target.X); writer.WriteInt("Y", target.Y); writer.WriteInt("Z", target.Z);
            writer.EndCompound();
        }
    }
    protected override void ReadAdditionalSave(NbtCompound tag)
    {
        DespawnDelay = tag.TryGetTagValue<int>("DespawnDelay", out var delay) ? delay : 48000;
        offers = MerchantOffers.Read(tag);
        if (tag.TryGetTag<NbtCompound>("WanderTarget", out var target) && target.TryGetTagValue<int>("X", out var x) &&
            target.TryGetTagValue<int>("Y", out var y) && target.TryGetTagValue<int>("Z", out var z)) WanderTarget = new Vector(x, y, z);
    }
}

internal sealed class TraderAvoidGoal(WanderingTrader trader) : NavigationGoal(trader, 0.5f)
{
    private IEntity? threat;
    private VectorD destination;
    public override bool CanUse()
    {
        threat = trader.GetEntitiesNear(12).Where(entity => entity.Type is EntityType.Zombie or EntityType.Husk or
            EntityType.ZombieVillager or EntityType.Drowned or EntityType.Evoker or EntityType.Vindicator or EntityType.Pillager or
            EntityType.Ravager or EntityType.Zoglin).Where(entity => trader.IsValidTarget(entity) && trader.CanSee(entity))
            .MinBy(entity => (entity.Position - trader.Position).MagnitudeSquared());
        if (threat == null || RandomPosition.Find(trader, 16, threat.Position) is not VectorD point) return false;
        destination = point;
        return (point - threat.Position).MagnitudeSquared() > (trader.Position - threat.Position).MagnitudeSquared();
    }
    public override void Start() => MoveTo(destination);
    public override bool CanContinue() => threat != null && trader.IsValidTarget(threat) && trader.IsInRange(threat, 16) && Navigation.IsNavigating;
}

internal sealed class TraderWanderGoal(WanderingTrader trader) : NavigationGoal(trader, 0.35f)
{
    public override bool CanUse() => trader.WanderTarget is { } target && (trader.Position - (VectorD)target).MagnitudeSquared() > 4;
    public override bool CanContinue() => CanUse();
    public override ValueTask TickAsync()
    {
        if (trader.WanderTarget is { } target) MoveTo((VectorD)target);
        return default;
    }
}
