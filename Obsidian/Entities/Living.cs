using Obsidian.API.Effects;
using Obsidian.API.Inventory;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Entities.AI;

namespace Obsidian.Entities;

public partial class Living : Entity, ILiving
{
    protected const byte LivingMetadataCount = 15;

    internal CombatDamageKind? IncomingDamageKind { get; private set; }

    // The last accepted source, retained after the scoped incoming source has been restored.
    internal CombatDamageKind? LastDamageKind { get; set; }

    internal ItemStack? IncomingWeapon { get; private set; }
    internal IEntity? IncomingAttacker { get; private set; }
    internal IEntity? IncomingDirectSource { get; private set; }
    internal int AcceptedDamageCount { get; set; }

    /// <summary>
    /// Damages the entity as <paramref name="kind"/> of damage that <paramref name="source"/> deals, through
    /// <paramref name="directSource"/> (a projectile, say) when that isn't the source itself.
    /// </summary>
    /// <param name="source">The attacker, or null for damage without an attacking entity.</param>
    /// <param name="sourcePosition">
    /// Where the damage comes from when that isn't the direct source's position, like an explosion's centre; players
    /// block with shields facing it (vanilla's <c>DamageSource.getSourcePosition</c>).
    /// </param>
    internal virtual async ValueTask DamageCombatAsync(
        IEntity? source,
        float amount,
        CombatDamageKind kind,
        ItemStack? weapon = null,
        IEntity? directSource = null,
        VectorD? sourcePosition = null)
    {
        var previousAttacker = IncomingAttacker;
        var previousKind = IncomingDamageKind;
        var previousWeapon = IncomingWeapon;
        var previousDirectSource = IncomingDirectSource;
        IncomingAttacker = ReferenceEquals(source, this) ? null : source;
        IncomingDamageKind = kind;
        IncomingWeapon = weapon;
        IncomingDirectSource = directSource;
        var acceptedBefore = this.AcceptedDamageCount;
        try
        {
            await DamageAsync(source ?? this, amount);
            if (this.AcceptedDamageCount != acceptedBefore)
                this.LastDamageKind = kind;
        }
        finally
        {
            IncomingAttacker = previousAttacker;
            IncomingDamageKind = previousKind;
            IncomingWeapon = previousWeapon;
            IncomingDirectSource = previousDirectSource;
        }
    }

    /// <summary>
    /// Damages a living entity outright, like <see cref="Entity.DamageAsync"/>, with its health write as the commit
    /// point (see <see cref="TryCommitDamage"/>). Mobs and players take damage through their own paths, which commit
    /// the same way.
    /// </summary>
    public override async ValueTask DamageAsync(IEntity source, float amount = 1.0f)
    {
        var level = this.Level;
        if (!float.IsFinite(amount) || amount <= 0 || this.Health <= 0 || source.Level != level)
            return;

        if (this.IsDamageImmune || this is IPlayer { GameMode: GameMode.Creative or GameMode.Spectator })
            return;

        if (this.TryCommitDamage(level, source, amount, out var healthTaken) && healthTaken)
            await this.ShowDamageAsync(level, source);
    }

    /// <summary>
    /// Whether the entity, and <paramref name="source"/>, are still in <paramref name="level"/>, the level it was hit
    /// in. A hit checks this again after each await before it changes anything: plugin hooks and item wear may suspend
    /// it, and the entity may travel meanwhile. Only <see cref="TryCommitDamage"/> keeps it so until the health write.
    /// </summary>
    private protected bool IsStillHitIn(ILevel level, IEntity source)
    {
        using (TransferLockOf(level).Enter())
            return this.StillIn(level, source);
    }

    /// <summary>
    /// A hit's commit point: under the transfer lock of <paramref name="level"/>, the level the entity was hit in,
    /// takes <paramref name="amount"/> off its health, unless it or <paramref name="source"/> left that level or it was
    /// removed. Travel commits under the same lock, so a hit that commits lands in the level it was dealt in.
    /// </summary>
    /// <remarks>
    /// Nothing but the health write happens under the lock: knockback, events, packets, death and drops follow once
    /// it's released, and only for a committed hit. A hit that isn't committed must change nothing.
    /// </remarks>
    /// <param name="healthTaken">
    /// Whether health was written: not for a hit absorbed entirely, or for an entity that died meanwhile.
    /// </param>
    /// <returns>Whether the hit was committed.</returns>
    private protected bool TryCommitDamage(ILevel level, IEntity source, float amount, out bool healthTaken)
    {
        healthTaken = false;
        using (TransferLockOf(level).Enter())
        {
            if (!this.StillIn(level, source))
                return false;

            if (!float.IsFinite(amount) || amount <= 0 || this.Health <= 0)
                return true;

            this.Health -= amount;
            healthTaken = true;
            return true;
        }
    }

    /// <summary>
    /// Whether the entity takes no damage at all now, whatever hits it: a player in Creative, say. A hit checks this
    /// again when it commits, as it may have awaited since it began.
    /// </summary>
    private protected virtual bool IsDamageImmune => false;

    // The guard every hit shares, after each await and at its commit: the entity and the hit's source are still in
    // level, and the entity can still be hurt. Call under level's transfer lock, which travel and removal take too.
    private bool StillIn(ILevel level, IEntity source) =>
        ReferenceEquals(this.Level, level)
        && ReferenceEquals(source.Level, level)
        && this.IsAmongEntitiesOf(level)
        && !this.IsDamageImmune;

    // Whether the entity is still among level's: a player among its players, which leaving the server or the level
    // takes it out of; anything else in its entity grid, which removal (DestroyEntityAsync among others) and travel
    // take it out of. Not its region's entities: those lag the position while the entity moves, and a hit that lands
    // mid-move (stepping onto magma, say) is still a hit in its level.
    private bool IsAmongEntitiesOf(ILevel level)
    {
        if (this is IPlayer)
            return level.Players.TryGetValue(this.Uuid, out var player) && ReferenceEquals(player, this);

        return level is WorldData.AbstractLevel owned
            && ReferenceEquals(this.Grid, owned.EntityGrid)
            && this is not Mob { IsRemoved: true };
    }

    /// <summary>
    /// What a hit that took health shows, once committed (see <see cref="TryCommitDamage"/>) in
    /// <paramref name="level"/>: its game events, the hurt animation and, for a player, its exhaustion, health and
    /// death.
    /// </summary>
    private protected async ValueTask ShowDamageAsync(ILevel level, IEntity source)
    {
        if (level is WorldData.AbstractLevel events)
        {
            events.EmitGameEvent(WorldData.MobGameEvent.EntityDamage, this.Position, this);
            if (this.Health <= 0)
                events.EmitGameEvent(WorldData.MobGameEvent.EntityDie, this.Position, this);
        }

        if (this is IPlayer attackedPlayer && !ReferenceEquals(source, this))
            Wolf.AlertOwnedWolves(attackedPlayer, source);

        var towardSource = Math.Atan2(source.Position.Z - this.Position.Z, source.Position.X - this.Position.X);
        this.PacketBroadcaster.QueuePacketToLevel(level, new HurtAnimationPacket
        {
            EntityId = this.EntityId,
            Yaw = (float)(towardSource * 180 / Math.PI - this.Yaw)
        });

        if (this is not IPlayer player)
            return;

        player.AddExhaustion(0.1f);
        var health = new SetHealthPacket(Math.Max(0, this.Health), player.FoodLevel, player.FoodSaturationLevel);
        await player.QueuePacketAsync(health);

        if (!player.Alive)
            await player.KillAsync(source, ChatMessage.Simple("You died xd"));
    }

    /// <summary>
    /// Vanilla's <c>Entity.thunderHit</c>: one more tick of fire (eight seconds of it when that brings the count to
    /// zero, which only an entity holding off fire reaches) and five lightning damage.
    /// </summary>
    internal override async ValueTask ThunderHitAsync(LightningBolt bolt)
    {
        this.fireTicks++;
        if (this.fireTicks == 0)
            this.Ignite(8);

        await this.DamageCombatAsync(null, 5, CombatDamageKind.Lightning);
    }

    public LivingBitMask LivingBitMask { get; set; }

    /// <summary>
    /// Vanilla's <c>PersistenceRequired</c>: the mob must never despawn (structure mobs such as witches and elder guardians).
    /// </summary>
    public bool PersistenceRequired { get; set; }

    public uint ActiveEffectColor { get; private set; }

    public bool AmbientPotionEffect { get; set; }

    public int AbsorbedArrows { get; set; }

    internal float AbsorptionHealth { get; set; }
    public int AbsorbtionAmount { get => (int)AbsorptionHealth; set => AbsorptionHealth = Math.Max(0, value); }

    public int AbsorbedStingers { get; set; }

    public Vector? BedBlockPosition { get; set; }

    public bool Alive => this.Health > 0f;

    public IReadOnlyDictionary<int, EffectWithCurrentDuration> ActivePotionEffects => activePotionEffectsView;

    private readonly ConcurrentDictionary<int, EffectWithCurrentDuration> activePotionEffects;

    // Serialize refreshes so a concurrent effect removal cannot leave a stale attribute modifier.
    private readonly System.Threading.Lock potionAttributeLock = new();

    // A live read-only view made once: mobs read their effects several times a tick, and each AsReadOnly allocated.
    private readonly IReadOnlyDictionary<int, EffectWithCurrentDuration> activePotionEffectsView;
    private int fireTicks;
    internal int FireTicks { get => fireTicks; set => fireTicks = value; }

    internal override int ContactFireTicks { get => this.fireTicks; set => this.fireTicks = value; }

    public void Ignite(int seconds)
    {
        if (!IsFireImmune && seconds > 0)
        {
            fireTicks = Math.Max(fireTicks, checked(seconds * 20));
            this.PowderedSnowTicks = 0;
        }
    }

    public Living()
    {
        activePotionEffects = new ConcurrentDictionary<int, EffectWithCurrentDuration>();
        activePotionEffectsView = activePotionEffects.AsReadOnly();
    }

    /// <summary>
    /// Applies the attribute modifiers of what the entity holds and wears, as they change; nothing for entities
    /// without that.
    /// </summary>
    internal virtual void UpdateEquipmentAttributes()
    {
    }

    public override async ValueTask TickAsync()
    {
        this.UpdateEquipmentAttributes();
        this.SendAttributeChanges();

        effectTicks++;
        foreach (var (potion, active) in activePotionEffects)
        {
            if (this is Mob && potion == (int)PotionEffect.Absorption - 1 && AbsorptionHealth <= 0)
            {
                RemovePotionEffect(potion);
                continue;
            }
            var data = active;
            var effectTime = data.CurrentDuration == -1 ? effectTicks : data.CurrentDuration;
            if (Alive && potion == (int)PotionEffect.Regeneration - 1 &&
                effectTime % Math.Max(1, 50 >> Math.Min(30, data.EffectData.Amplifier)) == 0)
            {
                Health = Math.Min(GetAttributeValue("minecraft:max_health"), Health + 1);
            }

            var poisonInterval = Math.Max(1, 25 >> Math.Min(30, data.EffectData.Amplifier));
            if (potion == (int)PotionEffect.Poison - 1 && effectTime % poisonInterval == 0 && Health > 1)
            {
                var amount = Math.Min(this is Witch ? 0.15f : 1, Health - 1);
                if (this is Mob mob)
                    await mob.DamageEnvironmentAsync(amount);
                else
                    await DamageCombatAsync(this, amount, CombatDamageKind.Magic);
            }
            if (potion == (int)PotionEffect.Wither - 1 && Alive &&
                effectTime % Math.Max(1, 40 >> Math.Min(30, data.EffectData.Amplifier)) == 0)
            {
                if (this is Mob mob)
                    await mob.DamageEnvironmentAsync(this is Witch ? 0.15f : 1);
                else
                    await DamageCombatAsync(this, 1, CombatDamageKind.Magic);
            }
            if (data.CurrentDuration != -1)
                data.CurrentDuration = Math.Max(0, data.CurrentDuration - 1);
            if (data.EffectData.HiddenEffect is { } pending)
            {
                data = data with { EffectData = data.EffectData with { HiddenEffect = TickEffectDuration(pending) } };
                activePotionEffects[potion] = data;
            }
            if (data.CurrentDuration == 0)
            {
                var hidden = data.EffectData.HiddenEffect;
                while (hidden is { Duration: 0 })
                    hidden = hidden.HiddenEffect;
                if (hidden == null)
                {
                    RemovePotionEffect(potion);
                }
                else
                {
                    activePotionEffects[potion] = new() { EffectData = hidden, CurrentDuration = hidden.Duration };
                    if (potion == (int)PotionEffect.JumpBoost - 1)
                        this.RefreshJumpBoostFallProtection();
                    if (this is Mob && potion == (int)PotionEffect.Absorption - 1)
                        AbsorptionHealth = Math.Min(AbsorptionHealth, 4 * (hidden.Amplifier + 1));
                    SendPotionEffect(hidden);
                }
            }
        }

        if (!Alive || this is not IPlayer and not Mob { HasAi: true } and not ArmorStand
            || this is ArmorStand { IsRemoved: true } || !Burning && fireTicks == 0)
            return;
        var terrain = new MobTerrain(Level);
        var feet = (Vector)(Position + new VectorD(0, 0.1f, 0)).Floor();
        if (Burning && fireTicks == 0 && this is not ArmorStand)
            fireTicks = 160;
        if (IsFireImmune || terrain.ContainsFluid(Dimension.CreateBBFromPosition(Position), water: true)
            || this is not ArmorStand && Level.LevelData.Raining && terrain.GetSkyLight(feet) == 15)
        {
            fireTicks = 0;
        }

        if (fireTicks > 0)
        {
            if (fireTicks % 20 == 0)
            {
                // The stand's damage override bypasses living Fire Resistance. Rain is handled after inside
                // effects, so a due burn can break the stand before rain gets a chance to extinguish it.
                if (this is ArmorStand stand)
                {
                    if (stand.Health > 0.5f)
                        await stand.DamageByBurningAsync(4);
                }
                else if (!HasPotionEffect((int)PotionEffect.FireResistance - 1))
                    await DamageCombatAsync(this, 1, CombatDamageKind.Fire);
            }
            fireTicks--;
        }
        if (Burning != fireTicks > 0)
        {
            Burning = fireTicks > 0;
            PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, new SetEntityDataPacket { EntityId = EntityId, Entity = this });
        }
    }

    private void RefreshJumpBoostFallProtection()
    {
        const string attribute = "minecraft:safe_fall_distance";
        const string modifierId = "minecraft:effect.jump_boost";
        lock (this.potionAttributeLock)
        {
            if (this.activePotionEffects.TryGetValue((int)PotionEffect.JumpBoost - 1, out var jump))
            {
                var modifier = new EntityAttributeModifier(
                    modifierId, jump.EffectData.Amplifier + 1, AttributeOperation.Add);
                this.SetModifier(attribute, modifier);
            }
            else
            {
                this.RemoveModifier(attribute, modifierId);
            }
        }
    }

    public bool HasPotionEffect(int effectId) => activePotionEffects.ContainsKey(effectId);

    internal void RestorePotionEffect(int id, int duration, int amplifier)
    {
        if (duration == -1 || duration > 0)
        {
            activePotionEffects[id] = new()
            {
                CurrentDuration = duration,
                EffectData = new() { Id = id, Duration = duration, Amplifier = amplifier }
            };
            if (id == (int)PotionEffect.JumpBoost - 1)
                this.RefreshJumpBoostFallProtection();
        }
    }

    public void ClearPotionEffects()
    {
        foreach (var (potion, _) in activePotionEffects)
        {
            RemovePotionEffect(potion);
        }
    }

    public void AddPotionEffect(int effectId, int duration, int amplifier = 0, EntityEffectFlags effect = EntityEffectFlags.None)
    {
        if (Type is EntityType.Wither or EntityType.EnderDragon)
            return;
        if (effectId == (int)PotionEffect.Poison - 1 && Type is EntityType.Zombie or EntityType.Husk or EntityType.Skeleton or EntityType.Stray or EntityType.Bogged or EntityType.Parched or EntityType.Spider or EntityType.CaveSpider or EntityType.WitherSkeleton or EntityType.Drowned or EntityType.ZombifiedPiglin or EntityType.Zoglin or EntityType.CamelHusk or EntityType.ZombieNautilus or EntityType.ZombieVillager or EntityType.Giant or EntityType.Phantom or EntityType.SkeletonHorse or EntityType.ZombieHorse ||
            effectId == (int)PotionEffect.Wither - 1 && Type == EntityType.WitherSkeleton ||
            effectId == (int)PotionEffect.Weakness - 1 && Type == EntityType.Parched)
        {
            return;
        }

        var data = new EffectWithCurrentDuration
        {
            CurrentDuration = duration,
            EffectData = new PotionEffectData
            {
                Id = effectId,
                Duration = duration,
                Amplifier = amplifier,
                Ambient = effect.HasFlag(EntityEffectFlags.IsAmbient),
                ShowIcon = effect.HasFlag(EntityEffectFlags.ShowIcon),
                ShowParticles = effect.HasFlag(EntityEffectFlags.ShowParticles)
            }
        };

        var updated = activePotionEffects.AddOrUpdate(effectId, data, (_, current) =>
        {
            var merged = MergeEffect(current.EffectData with { Duration = current.CurrentDuration }, data.EffectData);
            return new() { EffectData = merged, CurrentDuration = merged.Duration };
        });
        if (effectId == (int)PotionEffect.JumpBoost - 1)
            this.RefreshJumpBoostFallProtection();

        if (this is Mob && effectId == (int)PotionEffect.Absorption - 1)
            AbsorptionHealth = Math.Min(4 * (updated.EffectData.Amplifier + 1), Math.Max(AbsorptionHealth, 4 * (amplifier + 1)));
        SendPotionEffect(updated.EffectData with { Duration = updated.CurrentDuration });
    }

    internal override void WriteNbt(NbtCompound tag)
    {
        base.WriteNbt(tag);

        tag.Set(this.SaveAttributes());
        tag.Set(new NbtTag<float>("Health", this.Health));
        tag.Set(new NbtTag<float>("AbsorptionAmount", this.AbsorptionHealth));
        tag.Set(new NbtTag<bool>("PersistenceRequired", this.PersistenceRequired));
    }

    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);

        this.LoadAttributes(tag);

        if (tag.TryGetNumber<float>("Health", out var health))
            this.Health = health;
        if (tag.TryGetNumber<float>("AbsorptionAmount", out var absorption))
            this.AbsorptionHealth = float.IsFinite(absorption) ? Math.Max(0, absorption) : 0;

        this.PersistenceRequired = tag.TryGetBool("PersistenceRequired", out var persistent) && persistent;
    }

    public void RemovePotionEffect(int effectId)
    {
        this.PacketBroadcaster.QueuePacketToLevel(this.Level, new RemoveMobEffectPacket(EntityId, effectId));
        activePotionEffects.TryRemove(effectId, out _);
        if (effectId == (int)PotionEffect.JumpBoost - 1)
            this.RefreshJumpBoostFallProtection();
        if (this is Mob && effectId == (int)PotionEffect.Absorption - 1)
            AbsorptionHealth = 0;
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteEntityMetadataType(8, EntityMetadataType.Byte);
        writer.WriteByte(LivingBitMask);

        writer.WriteEntityMetadataType(9, EntityMetadataType.Float);
        writer.WriteSingle(Health);

        writer.WriteEntityMetadataType(10, EntityMetadataType.Particles);//This is a list of integers?
        writer.WriteVarInt(0);

        writer.WriteEntityMetadataType(11, EntityMetadataType.Boolean);
        writer.WriteBoolean(AmbientPotionEffect);
       
        writer.WriteEntityMetadataType(12, EntityMetadataType.VarInt);
        writer.WriteVarInt(AbsorbedArrows);

        writer.WriteEntityMetadataType(13, EntityMetadataType.VarInt);
        writer.WriteVarInt(AbsorbedStingers);

        writer.WriteEntityMetadataType(14, EntityMetadataType.OptionalBlockPos);
        writer.WriteOptional(BedBlockPosition);

        this.MetadataIndex = 15;
    }
}
