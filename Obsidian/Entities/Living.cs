using Obsidian.API.Effects;
using Obsidian.API.Inventory;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Entities.AI;

namespace Obsidian.Entities;

public partial class Living : Entity, ILiving
{
    internal CombatDamageKind? IncomingDamageKind { get; private set; }
    internal ItemStack? IncomingWeapon { get; private set; }
    internal IEntity? IncomingDirectSource { get; private set; }
    internal int AcceptedDamageCount { get; set; }

    /// <summary>
    /// Damages the entity as <paramref name="kind"/> of damage that <paramref name="source"/> deals, through
    /// <paramref name="directSource"/> (a projectile, say) when that isn't the source itself.
    /// </summary>
    /// <param name="sourcePosition">
    /// Where the damage comes from when that isn't the direct source's position, like an explosion's centre; players
    /// block with shields facing it (vanilla's <c>DamageSource.getSourcePosition</c>).
    /// </param>
    internal virtual async ValueTask DamageCombatAsync(
        IEntity source,
        float amount,
        CombatDamageKind kind,
        ItemStack? weapon = null,
        IEntity? directSource = null,
        VectorD? sourcePosition = null)
    {
        var previousKind = IncomingDamageKind;
        var previousWeapon = IncomingWeapon;
        var previousDirectSource = IncomingDirectSource;
        IncomingDamageKind = kind;
        IncomingWeapon = weapon;
        IncomingDirectSource = directSource;
        try
        { await DamageAsync(source, amount); }
        finally
        {
            IncomingDamageKind = previousKind;
            IncomingWeapon = previousWeapon;
            IncomingDirectSource = previousDirectSource;
    }
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

    // A live read-only view made once: mobs read their effects several times a tick, and each AsReadOnly allocated.
    private readonly IReadOnlyDictionary<int, EffectWithCurrentDuration> activePotionEffectsView;
    private int fireTicks;
    internal int FireTicks { get => fireTicks; set => fireTicks = value; }

    public void Ignite(int seconds)
    {
        if (!IsFireImmune && seconds > 0)
            fireTicks = Math.Max(fireTicks, checked(seconds * 20));
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
                Health = Math.Min(GetAttributeValue("minecraft:max_health"), Health + 1);
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
                    RemovePotionEffect(potion);
                else
                {
                    activePotionEffects[potion] = new() { EffectData = hidden, CurrentDuration = hidden.Duration };
                    if (this is Mob && potion == (int)PotionEffect.Absorption - 1)
                        AbsorptionHealth = Math.Min(AbsorptionHealth, 4 * (hidden.Amplifier + 1));
                    SendPotionEffect(hidden);
                }
            }
        }

        if (!Alive || this is not IPlayer and not Mob { HasAi: true } || !Burning && fireTicks == 0)
            return;
        var terrain = new MobTerrain(Level);
        var feet = (Vector)(Position + new VectorD(0, 0.1f, 0)).Floor();
        if (Burning && fireTicks == 0)
            fireTicks = 160;
        if (IsFireImmune || terrain.ContainsFluid(Dimension.CreateBBFromPosition(Position), water: true) ||
            Level.LevelData.Raining && terrain.GetSkyLight(feet) == 15)
            fireTicks = 0;
        if (fireTicks > 0)
        {
            if (fireTicks % 20 == 0 && !HasPotionEffect((int)PotionEffect.FireResistance - 1))
            {
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

    public bool HasPotionEffect(int effectId) => activePotionEffects.ContainsKey(effectId);

    internal void RestorePotionEffect(int id, int duration, int amplifier)
    {
        if (duration == -1 || duration > 0)
            activePotionEffects[id] = new()
            {
                CurrentDuration = duration,
                EffectData = new() { Id = id, Duration = duration, Amplifier = amplifier }
            };
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
            return;
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

        if (tag.TryGetTag<NbtTag<float>>("Health", out var health))
            this.Health = health.Value;
        if (tag.TryGetTag<NbtTag<float>>("AbsorptionAmount", out var absorption))
            this.AbsorptionHealth = float.IsFinite(absorption.Value) ? Math.Max(0, absorption.Value) : 0;

        this.PersistenceRequired = tag.TryGetBool("PersistenceRequired", out var persistent) && persistent;
    }

    public void RemovePotionEffect(int effectId)
    {
        this.PacketBroadcaster.QueuePacketToLevel(this.Level, new RemoveMobEffectPacket(EntityId, effectId));
        activePotionEffects.TryRemove(effectId, out _);
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
