using Obsidian.Entities.AI;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;

namespace Obsidian.Entities;

public partial class Mob
{
    protected virtual bool UsesAi => false;
    internal bool HasAi => UsesAi;
    internal Random Random { get; } = new();
    internal MobTerrain Terrain { get; private set; } = null!;
    internal long AiTick { get; private set; }
    internal bool InWater => Terrain.GetBlock((Vector)(Position + new VectorF(0, 0.1f, 0)).Floor())?.Material == Material.Water;
    internal bool InLava => Terrain.GetBlock((Vector)(Position + new VectorF(0, 0.1f, 0)).Floor())?.Material == Material.Lava;
    internal bool IsRemoved { get; private set; }
    internal IEntity? LastAttacker { get; private set; }
    internal long LastHurtTick { get; private set; } = -100;
    internal virtual float EyeHeight => Dimension.Height * 0.85f;
    internal VectorF EyePosition => Position + new VectorF(0, EyeHeight, 0);
    internal virtual float MovementSpeed => GetAttributeValue("minecraft:generic.movement_speed");
    internal virtual float JumpPower => 0.42f;
    internal float FollowRange => GetAttributeValue("minecraft:generic.follow_range");
    internal IEntity? AttackTarget { get; set; }
    internal IEntity? AlertedTarget { get; set; }
    public GoalSelector TargetGoals { get; } = new();
    public MoveControl MoveControl { get; protected set; }
    public LookControl LookControl { get; }
    public JumpControl JumpControl { get; } = new();
    private readonly GoalSelector goals = new();
    private bool initialized;
    private ILevel? aiLevel;
    private readonly Dictionary<int, bool> visibility = [];
    private float lastDamage;
    private long lastDamageTick = -100;
    private int deathTicks;
    private bool deathStarted;
    private long lastPlayerHurtTick = -1000;
    private Angle lastSentHeadYaw;
    private Angle lastSentYaw;
    private Angle lastSentPitch;
    private VectorF lastSentPosition;
    private bool hasSentPosition;
    private int despawnAge;

    public Mob()
    {
        MoveControl = new MoveControl(this);
        LookControl = new LookControl(this);
    }

    internal void InitializeAi(bool finalizeSpawn = true)
    {
        if (!UsesAi)
            return;

        if (initialized)
        {
            if (!ReferenceEquals(aiLevel, Level))
            {
                aiLevel = Level;
                Terrain = new MobTerrain(Level);
                goals.Cancel();
                TargetGoals.Cancel();
                (Navigator as Navigator)?.Stop();
                hasSentPosition = false;
            }
            return;
        }

        Terrain = new MobTerrain(Level);
        aiLevel = Level;
        Health = GetAttributeValue("minecraft:generic.max_health");
        BoundingBox = Dimension.CreateBBFromPosition(Position);
        GoalController ??= goals;
        if (this is PathfinderMob pathfinder)
            Navigator ??= new Navigator(pathfinder);
        initialized = true;
        RegisterGoals(goals, TargetGoals);
        if (finalizeSpawn)
            FinalizeSpawn();
    }

    protected virtual void RegisterGoals(GoalSelector actionGoals, GoalSelector targetGoals) { }
    protected virtual void FinalizeSpawn() { }
    protected virtual ValueTask TickMobAsync() => default;
    protected virtual ValueTask OnHurtAsync(IEntity source) => default;
    internal virtual ValueTask InteractAsync(IPlayer player, Hand hand) => default;

    internal async ValueTask<Mob> ConvertToAsync(EntityType type)
    {
        var replacement = Obsidian.Entities.Factories.EntitySpawner.CreateMob(Level, type)
            ?? throw new ArgumentOutOfRangeException(nameof(type));
        replacement.EntityId = Server.GetNextEntityId();
        replacement.Position = Position;
        replacement.InitializeAi(false);
        replacement.Yaw = Yaw;
        replacement.Pitch = Pitch;
        replacement.Motion = Motion;
        replacement.Health = Math.Min(Health, replacement.GetAttributeValue("minecraft:generic.max_health"));
        replacement.CustomName = CustomName;
        replacement.CustomNameVisible = CustomNameVisible;
        replacement.PersistenceRequired = PersistenceRequired;
        replacement.CanPickUpLoot = CanPickUpLoot;
        replacement.MobBitMask = MobBitMask;
        if (replacement is Zombie zombie && this is Zombie original)
        {
            zombie.IsBaby = original.IsBaby;
            zombie.CanBreakDoors = original.CanBreakDoors;
        }
        foreach (var (slot, item) in equipment)
        {
            replacement.equipment[slot] = item;
            replacement.equipmentDropChances[slot] = GetEquipmentDropChance(slot);
        }
        replacement.BoundingBox = replacement.Dimension.CreateBBFromPosition(Position);
        await RemoveAsync();
        Level.SpawnEntity(replacement);
        return replacement;
    }

    protected async ValueTask ConsumeInteractionItemAsync(IPlayer player, Hand hand)
    {
        if (player.Gamemode == Gamemode.Creative)
            return;
        var slot = hand == Hand.OffHand ? 45 : player.CurrentHeldItemSlot;
        player.Inventory.RemoveItem(slot, 1);
        await player.Client.QueuePacketAsync(new ContainerSetSlotPacket
        { ContainerId = 0, Slot = (short)slot, SlotData = player.Inventory.GetItem(slot) });
    }

    protected async ValueTask DamageInteractionToolAsync(IPlayer player, Hand hand, int amount = 1)
    {
        if (player.Gamemode == Gamemode.Creative)
            return;
        var slot = hand == Hand.OffHand ? 45 : player.CurrentHeldItemSlot;
        var item = player.Inventory.GetItem(slot);
        if (item == null || item.Unbreakable)
            return;
        var maximum = item.GetComponent<Obsidian.API.Inventory.DataComponents.SimpleDataComponent<int>>(DataComponentType.MaxDamage)?.Value ?? 0;
        if (maximum <= 0)
            return;
        var damage = Obsidian.API.Inventory.DataComponents.ComponentBuilder.Damage;
        damage.Value = item.Damage + amount;
        item[DataComponentType.Damage] = damage;
        if (damage.Value >= maximum)
            player.Inventory.SetItem(slot, null);
        await player.Client.QueuePacketAsync(new ContainerSetSlotPacket
        { ContainerId = 0, Slot = (short)slot, SlotData = player.Inventory.GetItem(slot) });
    }
    protected internal virtual float AttackDamage => GetAttributeValue("minecraft:generic.attack_damage");

    internal void SetAggressive(bool aggressive)
    {
        if (MobBitMask.HasFlag(MobBitmask.Agressive) == aggressive)
            return;
        MobBitMask = aggressive ? MobBitMask | MobBitmask.Agressive : MobBitMask & ~MobBitmask.Agressive;
        SynchronizeMetadata();
    }

    internal bool IsWithinMeleeRange(IEntity target)
    {
        var bounds = Dimension.CreateBBFromPosition(Position);
        var reach = MathF.Sqrt(2.04f) - 0.6f;
        return MobTerrain.Overlaps(new BoundingBox(bounds.Min - new VectorF(reach, 0, reach),
            bounds.Max + new VectorF(reach, 0, reach)), target.Dimension.CreateBBFromPosition(target.Position));
    }

    protected internal virtual async ValueTask PerformMeleeAttackAsync(IEntity target)
    {
        PacketBroadcaster.QueuePacketToLevelInRange(Level, Position,
            new AnimatePacket { EntityId = EntityId, Animation = EntityAnimationType.SwingMainArm }, EntityId);
        var damage = AttackDamage;
        if (target is IPlayer)
            damage = Level.LevelData.Difficulty switch
            {
                Difficulty.Peaceful => 0,
                Difficulty.Easy => MathF.Min(damage / 2 + 1, damage),
                Difficulty.Hard => damage * 1.5f,
                _ => damage
            };
        await target.DamageAsync(this, damage);
    }

    public override async ValueTask TickAsync()
    {
        await base.TickAsync();
        if (!UsesAi || IsRemoved)
            return;

        InitializeAi();
        AiTick++;
        visibility.Clear();
        if (Alive && this is not Animal && !PersistenceRequired && CustomName == null)
        {
            var nearest = Level.GetPlayersInRange(Position, float.MaxValue).Where(player => player.Gamemode != Gamemode.Spectator)
                .Select(player => (player.Position - Position).MagnitudeSquared()).DefaultIfEmpty(float.MaxValue).Min();
            if (nearest > 16384 && nearest < float.MaxValue || ++despawnAge > 600 && nearest > 1024 && nearest < float.MaxValue && Random.Next(800) == 0)
            {
                await RemoveAsync();
                return;
            }
            if (nearest < 1024)
                despawnAge = 0;
        }
        if (!Alive)
        {
            if (++deathTicks >= 20)
            {
                if (AiTick - lastPlayerHurtTick <= 120)
                    Level.SpawnExperienceOrbs(Position, (short)GetExperienceReward());
                await RemoveAsync();
            }
            return;
        }

        await TickMobAsync();
        if (IsRemoved)
            return;
        await TickEnvironmentAsync();
        if (!Alive)
            return;
        await PickupEquipmentAsync();

        if (MobBitMask.HasFlag(MobBitmask.NoAi))
        {
            goals.Cancel();
            TargetGoals.Cancel();
            (Navigator as Navigator)?.Stop();
            MoveControl.Stop();
        }
        else
        {
            var updateSelection = AiTick <= 1 || (AiTick + EntityId) % 2 == 0;
            await TargetGoals.TickAsync(updateSelection);
            if (GoalController is GoalSelector selector)
                await selector.TickAsync(updateSelection);
            (Navigator as Navigator)?.Tick();
            MoveControl.Tick();
            LookControl.Tick();
        }

        TickRidden();

        var oldPosition = Position;
        var position = EntityMovement.Move(this);
        LastPosition = oldPosition;
        if (Level is AbstractLevel level && !level.TryMoveEntity(this, oldPosition, position))
        {
            Motion = VectorF.Zero;
            (Navigator as Navigator)?.Stop();
            return;
        }

        Position = position;
        BoundingBox = Dimension.CreateBBFromPosition(position);
        UpdateRider();
        SynchronizeMovement(oldPosition);
    }

    private void SynchronizeMovement(VectorF oldPosition)
    {
        if (!hasSentPosition)
        {
            lastSentPosition = oldPosition;
            hasSentPosition = true;
        }

        var delta = (Vector)((Position - lastSentPosition) * 4096).Floor();
        var moved = delta.X != 0 || delta.Y != 0 || delta.Z != 0;
        var rotated = Yaw != lastSentYaw || Pitch != lastSentPitch;
        if (moved)
        {
            if (delta.X is < short.MinValue or > short.MaxValue || delta.Y is < short.MinValue or > short.MaxValue || delta.Z is < short.MinValue or > short.MaxValue)
            {
                PacketBroadcaster.BroadcastToLevelInRange(Level, Position, new TeleportEntityPacket
                {
                    EntityId = EntityId, Position = Position, Yaw = Yaw, Pitch = Pitch,
                    OnGround = MovementFlags.HasFlag(MovementFlags.OnGround)
                }, EntityId);
                lastSentPosition = Position;
            }
            else
            {
                PacketBroadcaster.BroadcastToLevelInRange(Level, Position, new MoveEntityPosRotPacket
                {
                    EntityId = EntityId, Delta = delta, Yaw = Yaw, Pitch = Pitch,
                    OnGround = MovementFlags.HasFlag(MovementFlags.OnGround)
                }, EntityId);
                lastSentPosition += (VectorF)delta / 4096;
            }
        }
        else if (rotated)
            PacketBroadcaster.BroadcastToLevelInRange(Level, Position, new MoveEntityRotPacket
            {
                EntityId = EntityId, Yaw = Yaw, Pitch = Pitch,
                OnGround = MovementFlags.HasFlag(MovementFlags.OnGround)
            }, EntityId);

        lastSentYaw = Yaw;
        lastSentPitch = Pitch;
        if (LookControl.HeadYaw != lastSentHeadYaw)
        {
            SetHeadRotation(LookControl.HeadYaw);
            lastSentHeadYaw = LookControl.HeadYaw;
        }
    }

    internal bool CanSee(IEntity entity)
    {
        if (entity.Level != Level)
            return false;
        if (!visibility.TryGetValue(entity.EntityId, out var visible))
        {
            visible = Terrain.HasLineOfSight(EyePosition, entity.Position + new VectorF(0,
                entity is Mob other ? other.EyeHeight : entity.Dimension.Height * 0.85f, 0));
            visibility[entity.EntityId] = visible;
        }
        return visible;
    }

    internal bool IsValidTarget(IEntity entity) => entity.Level == Level && entity.Health > 0 &&
        !ReferenceEquals(entity, this) && (entity is not Mob other || !other.IsRemoved) &&
        (entity is not IPlayer player || player.Gamemode is not Gamemode.Creative and not Gamemode.Spectator);

    internal void SynchronizeMetadata() => PacketBroadcaster.QueuePacketToLevelInRange(Level, Position,
        new SetEntityDataPacket { EntityId = EntityId, Entity = this }, EntityId);

    public override ValueTask DamageAsync(IEntity source, float amount = 1) => ApplyDamageAsync(source, amount, true);

    internal ValueTask DamageEnvironmentAsync(float amount) => ApplyDamageAsync(this, amount, false);

    private async ValueTask ApplyDamageAsync(IEntity source, float amount, bool applyArmor)
    {
        if (!UsesAi)
        {
            await base.DamageAsync(source, amount);
            return;
        }
        if (IsRemoved || !Alive || source.Level != Level || !float.IsFinite(amount) || amount <= 0)
            return;

        var incoming = amount;
        var recentlyHurt = AiTick - lastDamageTick < 10;
        if (recentlyHurt)
        {
            if (amount <= lastDamage)
                return;
            amount -= lastDamage;
        }
        else
        {
            var direction = Position - source.Position;
            var horizontal = MathF.Sqrt(direction.X * direction.X + direction.Z * direction.Z);
            if (horizontal > 0.0001f)
                Motion = new VectorF(Motion.X / 2 + direction.X / horizontal * 0.4f, 0.4f, Motion.Z / 2 + direction.Z / horizontal * 0.4f);
        }

        lastDamage = incoming;
        despawnAge = 0;
        if (source is IPlayer)
            lastPlayerHurtTick = AiTick;
        if (!ReferenceEquals(source, this))
        {
            LastAttacker = source;
            LastHurtTick = AiTick;
        }
        if (!recentlyHurt)
            lastDamageTick = AiTick;
        if (applyArmor)
        {
            var armor = GetAttributeValue("minecraft:generic.armor") + EquipmentArmor;
            var toughness = GetAttributeValue("minecraft:generic.armor_toughness") + EquipmentToughness;
            amount *= 1 - Math.Clamp(armor - amount / (2 + toughness / 4), armor / 5, 20) / 25;
            foreach (var slot in armorSlots)
                DamageEquipment(slot, Math.Max(1, (int)(incoming / 4)));
        }
        await base.DamageAsync(source, amount);
        await OnHurtAsync(source);
        if (Alive)
            PlayMobSound("hurt");
        SynchronizeMetadata();
        if (!Alive)
            await KillAsync(source);
    }

    public override async ValueTask RemoveAsync()
    {
        Dismount();
        IsRemoved = true;
        goals.Cancel();
        TargetGoals.Cancel();
        (Navigator as Navigator)?.Stop();
        await base.RemoveAsync();
    }

    protected virtual ValueTask OnDeathAsync(IEntity source) => default;
    protected virtual int GetExperienceReward() => 0;

    internal void SendEntityEvent(byte entityEvent) => PacketBroadcaster.QueuePacketToLevelInRange(Level, Position,
        new EntityEventPacket { EntityId = EntityId, Event = entityEvent }, EntityId);

    public override async ValueTask KillAsync(IEntity source)
    {
        if (!UsesAi)
        {
            await base.KillAsync(source);
            return;
        }
        if (deathStarted)
            return;
        Health = 0;
        deathStarted = true;
        goals.Cancel();
        TargetGoals.Cancel();
        (Navigator as Navigator)?.Stop();
        SendEntityEvent(3);
        PlayMobSound("death");
        await OnDeathAsync(source);
    }
}
