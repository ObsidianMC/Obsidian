using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Entities.AI;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:pillager")]
public sealed partial class Pillager : PathfinderMob
{
    internal bool Charging { get; set; }
    public Pillager() => Type = EntityType.Pillager;
    protected override bool UsesAi => true;
    internal override bool Hostile => true;
    protected override string? SoundName => "pillager";
    protected override SoundCategory MobSoundCategory => SoundCategory.Hostile;
    protected override int GetExperienceReward() => 5;
    protected override void FinalizeSpawn() => SetEquipment(EquipmentSlot.MainHand, ItemsRegistry.GetSingleItem(Material.Crossbow));
    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        actions.AddGoal(0, new FloatGoal(this));
        actions.AddGoal(3, new PillagerCrossbowGoal(this));
        actions.AddGoal(8, new RandomStrollGoal(this, 0.6f));
        actions.AddGoal(9, new LookAtPlayerGoal(this, 15));
        actions.AddGoal(10, new RandomLookAroundGoal(this));
        targets.AddGoal(1, new HurtByTargetGoal(this));
        targets.AddGoal(2, new NearestAttackableTargetGoal(this, entity => entity is IPlayer));
        targets.AddGoal(3, new NearestAttackableTargetGoal(this, entity => entity.Type is EntityType.Villager or EntityType.IronGolem or EntityType.WanderingTrader));
    }
    internal void SetCrossbowLoaded(bool loaded)
    {
        var weapon = GetEquipment(EquipmentSlot.MainHand);
        if (weapon.Type != Material.Crossbow) return;
        var copy = new ItemStack(weapon, weapon.Count);
        copy[DataComponentType.ChargedProjectiles] = ComponentBuilder.ChargedProjectiles with
        { Value = loaded ? [ItemsRegistry.GetSingleItem(Material.Arrow)] : [] };
        SetEquipment(EquipmentSlot.MainHand, copy);
    }
    internal void Shoot(IEntity target)
    {
        var origin = EyePosition - new VectorD(0, 0.1, 0);
        var delta = target.Position + new VectorD(0, target.Dimension.Height / 3, 0) - origin;
        delta.Y += Math.Sqrt(delta.X * delta.X + delta.Z * delta.Z) * 0.2;
        if (delta.Magnitude < 0.0001) return;
        var deviation = 0.0172275f * (14 - 4 * (int)Level.LevelData.Difficulty);
        var direction = delta / delta.Magnitude + new VectorD(Random.NextSingle() - Random.NextSingle(),
            Random.NextSingle() - Random.NextSingle(), Random.NextSingle() - Random.NextSingle()) * deviation;
        Level.SpawnEntity(new Arrow { Level = Level, Type = EntityType.Arrow, EntityId = Server.GetNextEntityId(),
            Position = origin, Owner = this, Motion = direction * 1.6, Damage = 2 });
        if (!Silent)
            PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, new Obsidian.Net.Packets.Play.Clientbound.SoundEntityPacket
            { EntityId = EntityId, SoundLocation = "minecraft:item.crossbow.shoot", Category = SoundCategory.Hostile,
                Volume = 1, Pitch = 1 / (Random.NextSingle() * 0.4f + 0.8f), Seed = Random.NextInt64() }, EntityId);
    }
    protected override ValueTask OnDeathAsync(IEntity source)
    {
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
            if (!GetEquipment(slot).IsAir && Random.NextSingle() < GetEquipmentDropChance(slot)) DropItem(GetEquipment(slot));
        return default;
    }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);
        writer.WriteEntityMetadataType(16, EntityMetadataType.Boolean);
        writer.WriteBoolean(false);
        writer.WriteEntityMetadataType(17, EntityMetadataType.Boolean);
        writer.WriteBoolean(Charging);
    }
}

internal sealed class PillagerCrossbowGoal(Pillager pillager) : NavigationGoal(pillager, 1)
{
    private int sightTicks;
    private int chargeTicks;
    private long shootAt;
    private bool loaded;
    public override GoalFlags Flags => GoalFlags.Move | GoalFlags.Look;
    public override bool RequiresUpdateEveryTick => true;
    public override bool CanUse() => pillager.GetEquipment(EquipmentSlot.MainHand).Type == Material.Crossbow &&
        pillager.AttackTarget is { } target && pillager.IsValidTarget(target);
    public override bool CanContinue() => CanUse();
    public override void Start() => pillager.SetAggressive(true);
    public override ValueTask TickAsync()
    {
        var target = pillager.AttackTarget!;
        var visible = pillager.CanSee(target);
        sightTicks = visible ? Math.Max(0, sightTicks) + 1 : Math.Min(0, sightTicks) - 1;
        pillager.LookControl.LookAt(target);
        if (!pillager.IsInRange(target, 8) || sightTicks < 5) MoveTo(target);
        else Navigation.Stop();
        if (loaded)
        {
            if (pillager.AiTick >= shootAt && visible)
            {
                pillager.Shoot(target);
                loaded = false;
                pillager.SetCrossbowLoaded(false);
            }
        }
        else if (pillager.Charging)
        {
            if (sightTicks < -60) SetCharging(false);
            else if (++chargeTicks >= 25)
            {
                SetCharging(false);
                loaded = true;
                pillager.SetCrossbowLoaded(true);
                shootAt = pillager.AiTick + 20 + pillager.Random.Next(20);
            }
        }
        else if (visible && sightTicks >= 5 && pillager.IsInRange(target, 8))
        {
            chargeTicks = 0;
            SetCharging(true);
        }
        return default;
    }
    private void SetCharging(bool value)
    {
        pillager.Charging = value;
        if (value) pillager.LivingBitMask |= LivingBitMask.HandActive;
        else pillager.LivingBitMask &= ~LivingBitMask.HandActive;
        pillager.SynchronizeMetadata();
    }
    public override void Stop()
    {
        base.Stop();
        sightTicks = chargeTicks = 0;
        loaded = false;
        SetCharging(false);
        pillager.SetCrossbowLoaded(false);
        pillager.SetAggressive(false);
    }
}
