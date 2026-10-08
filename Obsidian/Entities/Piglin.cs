using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Loot;
using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:piglin")]
public sealed partial class Piglin : PathfinderMob
{
    public bool IsBaby { get; set; }
    public bool IsImmuneToZombification { get; set; }
    internal int TimeInOverworld { get; set; }
    private int admireTicks;
    internal bool Charging { get; set; }
    public Piglin() => Type = EntityType.Piglin;
    protected override bool UsesAi => true;
    internal override bool Hostile => true;
    protected override float DimensionScale => IsBaby ? 0.5f : 1;
    internal override float MovementSpeed => base.MovementSpeed * (IsBaby ? 1.2f : 1);
    protected override string? SoundName => "piglin";
    protected override SoundCategory MobSoundCategory => SoundCategory.Hostile;
    protected override int GetExperienceReward() => IsBaby ? 0 : 5;
    protected internal override float AttackDamage => base.AttackDamage + GetWeaponDamage(GetEquipment(EquipmentSlot.MainHand));
    internal bool CanFight => !IsBaby && admireTicks == 0;
    protected override void FinalizeSpawn()
    {
        IsBaby |= Random.NextSingle() < 0.2f;
        if (!IsBaby) SetEquipment(EquipmentSlot.MainHand, ItemsRegistry.GetSingleItem(Random.Next(2) == 0 ? Material.GoldenSword : Material.Crossbow));
        foreach (var (slot, material) in new[] { (EquipmentSlot.Helmet, Material.GoldenHelmet), (EquipmentSlot.Chestplate, Material.GoldenChestplate),
            (EquipmentSlot.Leggings, Material.GoldenLeggings), (EquipmentSlot.Boots, Material.GoldenBoots) })
            if (!IsBaby && Random.NextSingle() < 0.1f) SetEquipment(slot, ItemsRegistry.GetSingleItem(material));
    }
    protected override void RegisterGoals(GoalSelector actions, GoalSelector targets)
    {
        ((Navigator)Navigator!).CanOpenDoors = true;
        actions.AddGoal(0, new FloatGoal(this));
        actions.AddGoal(1, new PiglinAdmireGoal(this));
        actions.AddGoal(2, new AvoidEntityGoal(this, target => target.Type is EntityType.ZombifiedPiglin or EntityType.Zoglin, 6, 1));
        actions.AddGoal(2, new AvoidEntityGoal(this, target => IsBaby && ReferenceEquals(target, LastAttacker), 8, 1.4f));
        actions.AddGoal(3, new PiglinCrossbowGoal(this));
        actions.AddGoal(3, new PiglinMeleeGoal(this));
        actions.AddGoal(5, new RandomStrollGoal(this, 0.6f));
        actions.AddGoal(6, new LookAtPlayerGoal(this, 8));
        actions.AddGoal(7, new RandomLookAroundGoal(this));
        targets.AddGoal(1, new HurtByTargetGoal(this));
        targets.AddGoal(2, new NearestAttackableTargetGoal(this, target => CanFight && target is IPlayer player && !WearsGold(player)));
        targets.AddGoal(3, new NearestAttackableTargetGoal(this, target => CanFight && (target.Type is EntityType.WitherSkeleton or EntityType.Wither)));
    }
    private static bool WearsGold(IPlayer player) => Enumerable.Range(5, 4).Any(slot => player.Inventory.GetItem(slot)?.Type is
        Material.GoldenHelmet or Material.GoldenChestplate or Material.GoldenLeggings or Material.GoldenBoots);
    internal override async ValueTask InteractAsync(IPlayer player, InteractionHand hand)
    {
        if (!CanFight || !Alive || IsRemoved || player.Level != Level || player.Health <= 0 || player.GameMode == GameMode.Spectator ||
            !IsInRange(player, 4) || !CanSee(player) || (hand == InteractionHand.OffHand ? player.GetOffHandItem() : player.GetHeldItem()) is not { Count: > 0, Type: Material.GoldIngot }) return;
        await ConsumeInteractionItemAsync(player, hand);
        BeginAdmiring();
    }
    private void BeginAdmiring()
    {
        admireTicks = 120;
        SetEquipment(EquipmentSlot.OffHand, ItemsRegistry.GetSingleItem(Material.GoldIngot));
        AttackTarget = null;
        SynchronizeMetadata();
    }
    protected override ValueTask OnHurtAsync(IEntity source)
    {
        if (admireTicks > 0)
        {
            admireTicks = 0;
            DropItem(GetEquipment(EquipmentSlot.OffHand));
            SetEquipment(EquipmentSlot.OffHand, ItemStack.Air);
        }
        foreach (var ally in GetEntitiesNear(16).OfType<Mob>().Where(ally => ally is Piglin { IsBaby: false } or PiglinBrute))
            if (ally.IsValidTarget(source)) ally.AlertedTarget = source;
        return default;
    }
    protected override async ValueTask TickMobAsync()
    {
        if (MobBitMask.HasFlag(MobBitmask.NoAi)) return;
        var safe = CodecRegistry.TryGetDimension(Level.DimensionName, out var dimension) ? dimension.Element.PiglinSafe : Level.DimensionName == "minecraft:the_nether";
        if (safe || IsImmuneToZombification) TimeInOverworld = 0;
        else if (++TimeInOverworld > 300)
        {
            PlayMobSound("converted_to_zombified");
            var zombie = (ZombifiedPiglin)await ConvertToAsync(EntityType.ZombifiedPiglin);
            zombie.AddPotionEffect((int)PotionEffect.Nausea - 1, 200);
            return;
        }
        if (admireTicks > 0)
        {
            if (--admireTicks == 0)
            {
                if (!IsBaby)
                {
                    SetEquipment(EquipmentSlot.OffHand, ItemStack.Air);
                    var table = LootTables.All["minecraft:gameplay/piglin_bartering"];
                    foreach (var stack in table.GetRandomItems(new LootContext { Random = table.CreateRandom(Random.NextInt64()), Origin = Position, ThisEntity = this })) DropItem(stack);
                }
                SynchronizeMetadata();
            }
            return;
        }
        if (AttackTarget == null && AiTick % 10 == 0 && (!IsBaby || GetEquipment(EquipmentSlot.OffHand).IsAir))
        {
            var gold = GetEntitiesNear(1.5f).OfType<ItemEntity>().FirstOrDefault(item => item.CanPickup && item.Item is { Count: > 0, Type: Material.GoldIngot });
            if (gold != null)
            {
                gold.Item.Count--;
                PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, new TakeItemEntityPacket { CollectedEntityId = gold.EntityId, CollectorEntityId = EntityId, PickupItemCount = 1 });
                if (gold.Item.Count == 0) await gold.RemoveAsync();
                else PacketBroadcaster.QueuePacketToLevelInRange(Level, gold.Position, new SetEntityDataPacket { EntityId = gold.EntityId, Entity = gold });
                BeginAdmiring();
            }
        }
    }
    internal void LoadCrossbow(bool loaded)
    {
        var weapon = GetEquipment(EquipmentSlot.MainHand);
        if (weapon.Type != Material.Crossbow) return;
        var copy = new ItemStack(weapon, weapon.Count);
        copy[DataComponentType.ChargedProjectiles] = ComponentBuilder.ChargedProjectiles with { Value = loaded ? [ItemsRegistry.GetSingleItem(Material.Arrow)] : [] };
        SetEquipment(EquipmentSlot.MainHand, copy);
    }
    internal void Shoot(IEntity target)
    {
        var origin = EyePosition - new VectorD(0, 0.1, 0);
        var delta = target.Position + new VectorD(0, target.Dimension.Height / 3, 0) - origin;
        delta.Y += Math.Sqrt(delta.X * delta.X + delta.Z * delta.Z) * 0.2;
        if (delta.Magnitude < 0.0001) return;
        var deviation = 0.0172275f * (14 - 4 * (int)Level.LevelData.Difficulty);
        var direction = delta / delta.Magnitude + new VectorD(Random.NextSingle() - Random.NextSingle(), Random.NextSingle() - Random.NextSingle(), Random.NextSingle() - Random.NextSingle()) * deviation;
        Level.SpawnEntity(new Arrow { Level = Level, EntityId = Server.GetNextEntityId(), Position = origin, Type = EntityType.Arrow, Motion = direction * 1.6, Owner = this });
        if (!Silent) PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, new SoundEntityPacket { EntityId = EntityId,
            SoundLocation = "minecraft:item.crossbow.shoot", Category = SoundCategory.Hostile, Volume = 1, Pitch = 1, Seed = Random.NextInt64() });
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
        writer.WriteEntityMetadataType(16, EntityMetadataType.Boolean); writer.WriteBoolean(IsImmuneToZombification);
        writer.WriteEntityMetadataType(17, EntityMetadataType.Boolean); writer.WriteBoolean(IsBaby);
        writer.WriteEntityMetadataType(18, EntityMetadataType.Boolean); writer.WriteBoolean(Charging);
        writer.WriteEntityMetadataType(19, EntityMetadataType.Boolean); writer.WriteBoolean(false);
    }
    protected override void WriteAdditionalSave(INbtWriter writer)
    {
        writer.WriteBool("IsBaby", IsBaby);
        writer.WriteBool("IsImmuneToZombification", IsImmuneToZombification);
        writer.WriteInt("TimeInOverworld", TimeInOverworld);
        writer.WriteInt("ObsidianAdmireTicks", admireTicks);
    }
    protected override void ReadAdditionalSave(NbtCompound tag)
    {
        IsBaby = tag.TryGetBool("IsBaby", out var baby) && baby;
        IsImmuneToZombification = tag.TryGetBool("IsImmuneToZombification", out var immune) && immune;
        TimeInOverworld = Math.Max(0, tag.TryGetTagValue<int>("TimeInOverworld", out var ticks) ? ticks : 0);
        admireTicks = Math.Clamp(tag.TryGetTagValue<int>("ObsidianAdmireTicks", out var admire) ? admire : 0, 0, 120);
    }
    internal bool Admiring => admireTicks > 0;
}

internal sealed class PiglinAdmireGoal(Piglin piglin) : Goal
{
    public override GoalFlags Flags => GoalFlags.Move;
    public override bool CanUse() => piglin.Admiring;
    public override void Start() { ((Navigator)piglin.Navigator!).Stop(); piglin.MoveControl.Stop(); }
}
internal sealed class PiglinMeleeGoal(Piglin piglin) : MeleeAttackGoal(piglin)
{
    public override bool CanUse() => piglin.CanFight && piglin.GetEquipment(EquipmentSlot.MainHand).Type != Material.Crossbow && base.CanUse();
    public override bool CanContinue() => piglin.CanFight && piglin.GetEquipment(EquipmentSlot.MainHand).Type != Material.Crossbow && base.CanContinue();
}
internal sealed class PiglinCrossbowGoal(Piglin piglin) : NavigationGoal(piglin, 1)
{
    private int chargeTicks;
    private long shootAt;
    private bool loaded;
    public override GoalFlags Flags => GoalFlags.Move | GoalFlags.Look;
    public override bool RequiresUpdateEveryTick => true;
    public override bool CanUse() => piglin.CanFight && piglin.GetEquipment(EquipmentSlot.MainHand).Type == Material.Crossbow && piglin.AttackTarget is { } target && piglin.IsValidTarget(target);
    public override void Start() => piglin.SetAggressive(true);
    public override ValueTask TickAsync()
    {
        var target = piglin.AttackTarget!;
        piglin.LookControl.LookAt(target);
        var visible = piglin.CanSee(target);
        if (!visible || !piglin.IsInRange(target, 8)) MoveTo(target);
        else Navigation.Stop();
        if (loaded)
        {
            if (piglin.AiTick >= shootAt && visible) { piglin.Shoot(target); loaded = false; piglin.LoadCrossbow(false); }
        }
        else if (piglin.Charging)
        {
            if (++chargeTicks >= 25) { SetCharging(false); loaded = true; piglin.LoadCrossbow(true); shootAt = piglin.AiTick + piglin.Random.Next(20, 40); }
        }
        else if (visible && piglin.IsInRange(target, 8)) { chargeTicks = 0; SetCharging(true); }
        return default;
    }
    private void SetCharging(bool value)
    {
        piglin.Charging = value;
        if (value) piglin.LivingBitMask |= LivingBitMask.HandActive;
        else piglin.LivingBitMask &= ~LivingBitMask.HandActive;
        piglin.SynchronizeMetadata();
    }
    public override void Stop() { base.Stop(); loaded = false; SetCharging(false); piglin.LoadCrossbow(false); piglin.SetAggressive(false); }
}
