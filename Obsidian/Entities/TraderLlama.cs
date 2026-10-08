using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:trader_llama")]
public sealed partial class TraderLlama : Llama
{
    public int DespawnDelay { get; set; } = 47999;
    public Guid TraderUuid { get; set; }
    internal WanderingTrader? Trader => GetEntitiesNear(64).OfType<WanderingTrader>().FirstOrDefault(trader => trader.Uuid == TraderUuid);
    public TraderLlama() => Type = EntityType.TraderLlama;
    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        base.RegisterGoals(actions, targets);
        actions.AddGoal(1, new TraderLlamaFollowGoal(this));
        targets.AddGoal(1, new TraderLlamaDefendGoal(this));
    }
    protected override async ValueTask TickMobAsync()
    {
        await base.TickMobAsync();
        if (HorseMask.HasFlag(HorseMask.Tamed) || HasRider || PersistenceRequired) return;
        DespawnDelay = Trader is { } trader ? trader.DespawnDelay - 1 : DespawnDelay - 1;
        if (DespawnDelay <= 0) await RemoveAsync();
    }
    protected override void WriteAdditionalSave(INbtWriter writer)
    {
        base.WriteAdditionalSave(writer);
        writer.WriteInt("DespawnDelay", DespawnDelay);
        if (TraderUuid != Guid.Empty) writer.WriteArray("ObsidianTrader", EntityNbt.UuidToInts(TraderUuid));
    }
    protected override void ReadAdditionalSave(NbtCompound tag)
    {
        base.ReadAdditionalSave(tag);
        DespawnDelay = tag.TryGetTagValue<int>("DespawnDelay", out var delay) ? delay : 47999;
        if (tag.TryGetTag<NbtArray<int>>("ObsidianTrader", out var trader) && trader.Count == 4) TraderUuid = EntityNbt.UuidFromInts(trader.GetArray());
        else if (tag.TryGetTag<NbtCompound>("leash", out var leash) && leash.TryGetTag<NbtArray<int>>("UUID", out var owner) && owner.Count == 4)
            TraderUuid = EntityNbt.UuidFromInts(owner.GetArray());
    }
}

internal sealed class TraderLlamaFollowGoal(TraderLlama llama) : NavigationGoal(llama, 2)
{
    public override bool CanUse() => !llama.HasRider && llama.Trader is { Alive: true } trader && !llama.IsInRange(trader, 2);
    public override ValueTask TickAsync()
    {
        if (llama.Trader is { } trader) MoveTo(trader);
        return default;
    }
}

internal sealed class TraderLlamaDefendGoal(TraderLlama llama) : Goal
{
    private long handledHurtTick = -100;
    public override GoalFlags Flags => GoalFlags.Target;
    public override bool CanUse() => llama.Trader is { LastAttacker: { } attacker } trader &&
        trader.LastHurtTick != handledHurtTick && llama.IsValidTarget(attacker);
    public override void Start()
    {
        if (llama.Trader is not { } trader) return;
        handledHurtTick = trader.LastHurtTick;
        llama.AttackTarget = trader.LastAttacker;
    }
    public override bool CanContinue() => llama.AttackTarget is { } target && llama.IsValidTarget(target);
    public override void Stop() => llama.AttackTarget = null;
}
