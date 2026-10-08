using Obsidian.Entities.AI;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:guardian")]
public partial class Guardian : PathfinderMob
{
    public Guardian() => Type = EntityType.Guardian;
    protected override bool UsesAi => true;
    internal override bool Hostile => true;
    internal override bool SwimmingNavigation => true;
    protected override string? SoundName => this is ElderGuardian ? "elder_guardian" : "guardian";
    protected override SoundCategory MobSoundCategory => SoundCategory.Hostile;
    protected override int GetExperienceReward() => 10;
    internal virtual int AttackDuration => 80;
    internal int BeamTarget { get; set; }
    private bool moving;
    protected override VectorD Travel() => VolumeMovement.Travel(this, InWater);
    protected override ValueTask TickAirSupplyAsync() { Air = 300; return default; }

    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        actions.AddGoal(4, new GuardianAttackGoal(this));
        actions.AddGoal(7, new RandomStrollGoal(this, 1));
        actions.AddGoal(8, new LookAtPlayerGoal(this, 8));
        actions.AddGoal(9, new RandomLookAroundGoal(this));
        targets.AddGoal(1, new NearestAttackableTargetGoal(this, target => (target.Type is EntityType.Player or EntityType.Squid or EntityType.GlowSquid or EntityType.Axolotl) &&
            (target.Position - Position).MagnitudeSquared() > 9));
    }
    protected override ValueTask TickMobAsync()
    {
        var swimming = InWater && BeamTarget == 0 && Navigator is Navigator { IsNavigating: true };
        if (moving != swimming) { moving = swimming; SynchronizeMetadata(); }
        if (!MobBitMask.HasFlag(MobBitmask.NoAi) && !InWater && MovementFlags.HasFlag(MovementFlags.OnGround))
        {
            Motion += new VectorD((Random.NextSingle() - 0.5f) * 0.8f, 0.5f, (Random.NextSingle() - 0.5f) * 0.8f);
            Yaw = Random.NextSingle() * 360;
            PlayMobSound("flop");
        }
        return default;
    }
    protected override async ValueTask OnHurtAsync(IEntity source)
    {
        if (!moving && !ReferenceEquals(source, this) && source is Living && IsValidTarget(source))
            await source.DamageAsync(this, 2);
    }
    protected override ValueTask OnDeathAsync(IEntity source)
    {
        DropItem(Material.PrismarineShard, Random.Next(3));
        var bonus = Random.Next(5);
        if (bonus < 2) DropItem(Burning ? Material.CookedCod : Material.Cod);
        else if (bonus < 4) DropItem(Material.PrismarineCrystals);
        if (source is IPlayer && Random.NextSingle() < 0.025f)
        {
            var fish = Random.Next(100);
            DropItem(fish < 60 ? Burning ? Material.CookedCod : Material.Cod : fish < 85 ? Burning ? Material.CookedSalmon : Material.Salmon :
                fish < 87 ? Material.TropicalFish : Material.Pufferfish);
        }
        return default;
    }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(16, EntityMetadataType.Boolean);
        writer.WriteBoolean(moving);
        writer.WriteEntityMetadataType(17, EntityMetadataType.VarInt);
        writer.WriteVarInt(BeamTarget);
    }
}

internal sealed class GuardianAttackGoal(Guardian guardian) : Goal
{
    private int chargeTicks;
    public override GoalFlags Flags => GoalFlags.Move | GoalFlags.Look;
    public override bool RequiresUpdateEveryTick => true;
    public override bool CanUse() => guardian.AttackTarget is { } target && guardian.IsValidTarget(target);
    public override bool CanContinue() => CanUse() && (guardian is ElderGuardian ||
        (guardian.AttackTarget!.Position - guardian.Position).MagnitudeSquared() > 9);
    public override void Start()
    {
        chargeTicks = -10;
        ((Navigator)guardian.Navigator!).Stop();
        guardian.MoveControl.Stop();
    }
    public override async ValueTask TickAsync()
    {
        var target = guardian.AttackTarget!;
        guardian.LookControl.LookAt(target);
        guardian.MoveControl.Stop();
        if (!guardian.CanSee(target)) { guardian.AttackTarget = null; return; }
        if (++chargeTicks == 0)
        {
            guardian.BeamTarget = target.EntityId;
            guardian.SynchronizeMetadata();
            guardian.SendEntityEvent(21);
        }
        if (chargeTicks < guardian.AttackDuration) return;
        var damage = guardian.AttackDamage;
        if (target is IPlayer)
            damage = guardian.Level.LevelData.Difficulty switch
            {
                Difficulty.Peaceful => 0,
                Difficulty.Easy => MathF.Min(damage / 2 + 1, damage),
                Difficulty.Hard => damage * 1.5f,
                _ => damage
            };
        await target.DamageAsync(guardian, damage);
        guardian.AttackTarget = null;
    }
    public override void Stop()
    {
        guardian.BeamTarget = 0;
        guardian.AttackTarget = null;
        guardian.SynchronizeMetadata();
    }
}

[MinecraftEntity("minecraft:elder_guardian")]
public sealed partial class ElderGuardian : Guardian
{
    public ElderGuardian() => Type = EntityType.ElderGuardian;
    protected override bool CanDespawn => false;
    internal override int AttackDuration => 60;
    protected override void FinalizeSpawn() => PersistenceRequired = true;
    protected override async ValueTask TickMobAsync()
    {
        await base.TickMobAsync();
        if (MobBitMask.HasFlag(MobBitmask.NoAi) || (AiTick + EntityId) % 1200 != 0) return;
        foreach (var player in Level.GetPlayersInRange(Position, 50).OfType<Player>().Where(player =>
            player.GameMode is GameMode.Survival or GameMode.Adventure))
        {
            if (player.ActivePotionEffects.TryGetValue((int)PotionEffect.MiningFatigue - 1, out var effect) &&
                effect.EffectData.Amplifier >= 2 && effect.CurrentDuration >= 1200) continue;
            player.AddPotionEffect((int)PotionEffect.MiningFatigue - 1, 6000, 2, EntityEffectFlags.ShowParticles | EntityEffectFlags.ShowIcon);
            await player.Client.QueuePacketAsync(new GameEventPacket(ChangeGameStateReason.PlayElderGuardianMobAppearance, Silent ? 0 : 1));
        }
    }
    protected override async ValueTask OnDeathAsync(IEntity source)
    {
        await base.OnDeathAsync(source);
        if (source is IPlayer) DropItem(Material.WetSponge);
        if (Random.Next(5) == 0) DropItem(Material.TideArmorTrimSmithingTemplate);
    }
}
