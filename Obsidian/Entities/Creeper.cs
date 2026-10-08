using Obsidian.Entities.AI;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:creeper")]
public sealed partial class Creeper : PathfinderMob
{
    public Creeper() => Type = EntityType.Creeper;
    public bool Powered { get; set; }
    public bool Ignited { get; set; }
    public int Fuse { get; set; } = 30;
    public int ExplosionRadius { get; set; } = 3;
    public int FuseTicks { get; internal set; }
    public int Swell { get; private set; } = -1;
    protected override bool UsesAi => true;
    protected override string? SoundName => "creeper";
    protected override SoundCategory MobSoundCategory => SoundCategory.Hostile;
    protected override int GetExperienceReward() => 5;

    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        actions.AddGoal(1, new FloatGoal(this));
        actions.AddGoal(2, new CreeperSwellGoal(this));
        actions.AddGoal(3, new AvoidEntityGoal(this, entity => entity.Type is EntityType.Cat or EntityType.Ocelot, 6, 1.2f));
        actions.AddGoal(4, new MeleeAttackGoal(this));
        actions.AddGoal(5, new RandomStrollGoal(this, 0.8f));
        actions.AddGoal(6, new LookAtPlayerGoal(this, 8));
        actions.AddGoal(6, new RandomLookAroundGoal(this));
        targets.AddGoal(1, new NearestAttackableTargetGoal(this, entity => entity is IPlayer));
        targets.AddGoal(2, new HurtByTargetGoal(this));
    }

    protected internal override ValueTask PerformMeleeAttackAsync(IEntity target) => default;

    internal override async ValueTask InteractAsync(IPlayer player, InteractionHand hand)
    {
        if (!Alive || IsRemoved || player.Level != Level || player.Health <= 0 || player.GameMode == GameMode.Spectator ||
            !IsInRange(player, 4) || !CanSee(player))
            return;
        var held = hand == InteractionHand.OffHand ? player.GetOffHandItem() : player.GetHeldItem();
        if (held is not { Count: > 0 } || held.Type is not Material.FlintAndSteel and not Material.FireCharge)
            return;
        Ignited = true;
        SynchronizeMetadata();
        if (held.Type == Material.FlintAndSteel)
            await DamageInteractionToolAsync(player, hand);
        else if (player.GameMode != GameMode.Creative)
        {
            player.Inventory.RemoveItem(hand == InteractionHand.OffHand ? 45 : player.CurrentHeldItemSlot, 1);
            var slot = hand == InteractionHand.OffHand ? 45 : player.CurrentHeldItemSlot;
            await player.Client.QueuePacketAsync(new Obsidian.Net.Packets.Play.Clientbound.ContainerSetSlotPacket
            { ContainerId = 0, Slot = (short)slot, SlotData = player.Inventory.GetItem(slot) });
        }
    }

    protected override async ValueTask TickMobAsync()
    {
        if (Level.LevelData.Difficulty == Difficulty.Peaceful)
        {
            await RemoveAsync();
            return;
        }
        var swell = Ignited || AttackTarget is { } target && IsValidTarget(target) && CanSee(target) &&
            (target.Position - Position).MagnitudeSquared() < (Swell > 0 ? 49 : 9) ? 1 : -1;
        if (Swell != swell)
        {
            if (swell > 0 && FuseTicks == 0)
                PlayMobSound("primed");
            Swell = swell;
            SynchronizeMetadata();
        }
        if (Swell > 0)
            (Navigator as Navigator)?.Stop();
        FuseTicks = Math.Clamp(FuseTicks + Swell, 0, Fuse);
        if (FuseTicks >= Fuse && Level is Obsidian.WorldData.AbstractLevel level)
        {
            await level.ExplodeAsync(this, Powered ? ExplosionRadius * 2 : ExplosionRadius);
            await RemoveAsync();
        }
    }

    protected override ValueTask OnDeathAsync(IEntity source)
    {
        DropItem(Material.Gunpowder, Random.Next(3));
        if (source is Skeleton)
        {
            Material[] discs = [Material.MusicDisc13, Material.MusicDiscCat, Material.MusicDiscBlocks, Material.MusicDiscChirp,
                Material.MusicDiscFar, Material.MusicDiscMall, Material.MusicDiscMellohi, Material.MusicDiscStal,
                Material.MusicDiscStrad, Material.MusicDiscWard, Material.MusicDisc11, Material.MusicDiscWait];
            DropItem(discs[Random.Next(discs.Length)]);
        }
        return default;
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(16, EntityMetadataType.VarInt);
        writer.WriteVarInt(Swell);
        writer.WriteEntityMetadataType(17, EntityMetadataType.Boolean);
        writer.WriteBoolean(Powered);
        writer.WriteEntityMetadataType(18, EntityMetadataType.Boolean);
        writer.WriteBoolean(Ignited);
    }
}

internal sealed class CreeperSwellGoal(Creeper creeper) : Goal
{
    public override GoalFlags Flags => GoalFlags.Move;
    public override bool RequiresUpdateEveryTick => true;
    public override bool CanUse() => creeper.Swell > 0;
    public override void Start() => (creeper.Navigator as Navigator)?.Stop();
    public override ValueTask TickAsync()
    {
        (creeper.Navigator as Navigator)?.Stop();
        return default;
    }
}

internal sealed class AvoidEntityGoal(PathfinderMob mob, Func<IEntity, bool> predicate, float range, float speed) : NavigationGoal(mob, speed)
{
    private IEntity? threat;
    private VectorD destination;
    public override bool CanUse()
    {
        threat = mob.GetEntitiesNear(range).FirstOrDefault(entity => entity.Health > 0 && predicate(entity));
        if (threat == null || RandomPosition.Find(mob, 16, threat.Position) is not VectorD point)
            return false;
        destination = point;
        return (point - threat.Position).MagnitudeSquared() > (mob.Position - threat.Position).MagnitudeSquared();
    }
    public override void Start() => MoveTo(destination);
    public override bool CanContinue() => threat != null && Navigation.IsNavigating;
}
