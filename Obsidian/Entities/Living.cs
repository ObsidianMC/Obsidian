using Obsidian.API.Effects;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Entities.AI;

namespace Obsidian.Entities;

public class Living : Entity, ILiving
{
    public LivingBitMask LivingBitMask { get; set; }

    public uint ActiveEffectColor { get; private set; }

    public bool AmbientPotionEffect { get; set; }

    public int AbsorbedArrows { get; set; }

    public int AbsorbtionAmount { get; set; }

    public int AbsorbedStingers { get; set; }

    public Vector? BedBlockPosition { get; set; }

    public bool Alive => this.Health > 0f;

    public IReadOnlyDictionary<int, EffectWithCurrentDuration> ActivePotionEffects => activePotionEffects.AsReadOnly();

    private readonly ConcurrentDictionary<int, EffectWithCurrentDuration> activePotionEffects;
    private int fireTicks;

    public void Ignite(int seconds)
    {
        if (!IsFireImmune && seconds > 0)
            fireTicks = Math.Max(fireTicks, checked(seconds * 20));
    }

    public Living()
    {
        activePotionEffects = new ConcurrentDictionary<int, EffectWithCurrentDuration>();
    }

    public override async ValueTask TickAsync()
    {
        foreach (var (potion, data) in activePotionEffects)
        {
            data.CurrentDuration--;

            if (data.CurrentDuration <= 0)
            {
                RemovePotionEffect(potion);
            }
        }

        if (!Alive || this is not Player and not Mob { HasAi: true })
            return;
        var terrain = new MobTerrain(Level);
        var feet = (Vector)(Position + new VectorF(0, 0.1f, 0)).Floor();
        if (Burning && fireTicks == 0)
            fireTicks = 160;
        if (IsFireImmune || terrain.GetBlock(feet)?.Material == Material.Water ||
            Level.LevelData.Raining && terrain.GetSkyLight(feet) == 15)
            fireTicks = 0;
        if (fireTicks > 0)
        {
            if (fireTicks % 20 == 0)
            {
                if (this is Mob mob)
                    await mob.DamageEnvironmentAsync(1);
                else
                    await DamageAsync(this, 1);
            }
            fireTicks--;
        }
        if (Burning != fireTicks > 0)
        {
            Burning = fireTicks > 0;
            PacketBroadcaster.QueuePacketToLevelInRange(Level, Position, new SetEntityDataPacket { EntityId = EntityId, Entity = this }, EntityId);
        }
    }

    public bool HasPotionEffect(int effectId) => activePotionEffects.ContainsKey(effectId);

    public void ClearPotionEffects()
    {
        foreach (var (potion, _) in activePotionEffects)
        {
            RemovePotionEffect(potion);
        }
    }

    public void AddPotionEffect(int effectId, int duration, int amplifier = 0, EntityEffectFlags effect = EntityEffectFlags.None)
    {
        this.PacketBroadcaster.QueuePacketToLevel(this.Level, new UpdateMobEffectPacket(EntityId, effectId, duration)
        {
            Amplifier = amplifier,
            Flags = effect
        });

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

        activePotionEffects.AddOrUpdate(effectId, _ => data, (_, _) => data);
    }

    public void RemovePotionEffect(int effectId)
    {
        this.PacketBroadcaster.QueuePacketToLevel(this.Level, new RemoveMobEffectPacket(EntityId, effectId));
        activePotionEffects.TryRemove(effectId, out _);
    }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        this.WriteEntityMetadataType(writer, EntityMetadataType.Byte);
        writer.WriteByte(LivingBitMask);

        this.WriteEntityMetadataType(writer, EntityMetadataType.Float);
        writer.WriteSingle(Health);

        this.WriteEntityMetadataType(writer, EntityMetadataType.Particles);//This is a list of integers?
        writer.WriteVarInt(0);

        this.WriteEntityMetadataType(writer, EntityMetadataType.Boolean);
        writer.WriteBoolean(AmbientPotionEffect);
       
        this.WriteEntityMetadataType(writer, EntityMetadataType.VarInt);
        writer.WriteVarInt(AbsorbedArrows);

        this.WriteEntityMetadataType(writer, EntityMetadataType.VarInt);
        writer.WriteVarInt(AbsorbedStingers);

        this.WriteEntityMetadataType(writer, EntityMetadataType.OptionalBlockPos);
        writer.WriteOptional(BedBlockPosition);
    }
}
