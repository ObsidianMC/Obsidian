using Obsidian.API.Inventory;
using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:armadillo")]
public sealed partial class Armadillo : FarmAnimal
{
    private int state;
    private int stateTicks;
    private long safeAt;
    private int scuteTime;
    public Armadillo() => Type = EntityType.Armadillo;
    protected override string? SoundName => "armadillo";
    protected override bool CanEat(ItemStack? item) => state == 0 && item is { Count: > 0, Type: Material.SpiderEye };
    protected override float DimensionScale => IsBaby ? 0.6f : 1;
    protected override void FinalizeSpawn() => scuteTime = Random.Next(6000, 12001);
    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        base.RegisterGoals(actions, targets);
        actions.AddGoal(0, new ArmadilloShellGoal(this));
    }
    internal bool Rolled => state != 0;
    public override ValueTask DamageAsync(IEntity source, float amount = 1) => base.DamageAsync(source, Rolled ? Math.Max(0, (amount - 1) / 2) : amount);
    private void SetState(int value)
    {
        if (state == value) return;
        state = value;
        stateTicks = 0;
        SynchronizeMetadata();
        if (value == 1) PlayMobSound("roll");
        if (value == 3) PlayMobSound("unroll_start");
    }
    protected override ValueTask OnHurtAsync(IEntity source) { safeAt = AiTick + 100; SetState(1); return default; }
    protected override async ValueTask TickMobAsync()
    {
        await base.TickMobAsync();
        if (!IsBaby && --scuteTime <= 0) { DropItem(Material.ArmadilloScute); PlayMobSound("scute_drop"); scuteTime = Random.Next(6000, 12001); }
        if (MobBitMask.HasFlag(MobBitmask.NoAi)) return;
        stateTicks++;
        if (AiTick % 5 == 0 && !InWater && !InLava && GetEntitiesNear(7).Any(entity =>
            Math.Abs(entity.Position.Y - Position.Y) <= 2 && (entity is Entity { Sprinting: true } || entity.Type is EntityType.Zombie or EntityType.Husk or EntityType.Drowned or EntityType.Skeleton or EntityType.Stray or EntityType.WitherSkeleton or EntityType.ZombifiedPiglin)))
        { safeAt = AiTick + 100; if (state is 0 or 3) SetState(1); }
        if (InWater || InLava) SetState(0);
        else if (state == 1 && stateTicks >= 10) SetState(2);
        else if (state == 2 && AiTick >= safeAt) SetState(3);
        else if (state == 3 && stateTicks >= 30) { SetState(0); PlayMobSound("unroll_finish"); }
        else if (state == 2 && Random.Next(400) == 0) SendEntityEvent(64);
    }
    internal override async ValueTask FeedAsync(IPlayer player, InteractionHand hand)
    {
        if (!Alive || IsRemoved || player.Level != Level || player.GameMode == GameMode.Spectator || !IsInRange(player, 4) || !CanSee(player)) return;
        var item = hand == InteractionHand.OffHand ? player.GetOffHandItem() : player.GetHeldItem();
        if (!IsBaby && item is { Count: > 0, Type: Material.Brush })
        { DropItem(Material.ArmadilloScute); PlayMobSound("brush"); await DamageInteractionToolAsync(player, hand, 16); }
        else await base.FeedAsync(player, hand);
    }
    public override void Write(INetStreamWriter writer)
    { base.Write(writer); writer.WriteEntityMetadataType(17, EntityMetadataType.ArmadilloState); writer.WriteVarInt(state); }
    protected override void WriteAdditionalSave(INbtWriter writer)
    { writer.WriteString("state", new[] { "idle", "rolling", "scared", "unrolling" }[state]); writer.WriteInt("scute_time", scuteTime); }
    protected override void ReadAdditionalSave(NbtCompound tag)
    {
        state = tag.TryGetTagValue<string>("state", out var value) ? Math.Max(0, Array.IndexOf(new[] { "idle", "rolling", "scared", "unrolling" }, value)) : 0;
        scuteTime = tag.TryGetTagValue<int>("scute_time", out var ticks) ? Math.Clamp(ticks, 0, 12000) : Random.Next(6000, 12001);
        safeAt = AiTick + 100;
    }
}
internal sealed class ArmadilloShellGoal(Armadillo armadillo) : Goal
{
    public override GoalFlags Flags => GoalFlags.Move | GoalFlags.Look | GoalFlags.Jump;
    public override bool CanUse() => armadillo.Rolled;
    public override void Start() { ((Navigator)armadillo.Navigator!).Stop(); armadillo.MoveControl.Stop(); }
}
