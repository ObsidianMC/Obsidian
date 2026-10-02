using Obsidian.API.Effects;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.Entities;

public class Living : Entity, ILiving
{
    public LivingBitMask LivingBitMask { get; set; }

    /// <summary>
    /// Vanilla's <c>PersistenceRequired</c>: the mob must never despawn (structure mobs such as witches and elder guardians).
    /// </summary>
    public bool PersistenceRequired { get; set; }

    public uint ActiveEffectColor { get; private set; }

    public bool AmbientPotionEffect { get; set; }

    public int AbsorbedArrows { get; set; }

    public int AbsorbtionAmount { get; set; }

    public int AbsorbedStingers { get; set; }

    public Vector? BedBlockPosition { get; set; }

    public bool Alive => this.Health > 0f;

    public IReadOnlyDictionary<int, EffectWithCurrentDuration> ActivePotionEffects => activePotionEffects.AsReadOnly();

    private readonly ConcurrentDictionary<int, EffectWithCurrentDuration> activePotionEffects;

    public Living()
    {
        activePotionEffects = new ConcurrentDictionary<int, EffectWithCurrentDuration>();
    }

    public override ValueTask TickAsync()
    {
        foreach (var (potion, data) in activePotionEffects)
        {
            data.CurrentDuration--;

            if (data.CurrentDuration <= 0)
            {
                RemovePotionEffect(potion);
            }
        }

        return default;
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

    internal override void WriteNbt(NbtCompound tag)
    {
        base.WriteNbt(tag);

        tag.Set(new NbtTag<float>("Health", this.Health));
        tag.Set(new NbtTag<float>("AbsorptionAmount", this.AbsorbtionAmount));
        tag.Set(new NbtTag<bool>("PersistenceRequired", this.PersistenceRequired));
    }

    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);

        if (tag.TryGetTag<NbtTag<float>>("Health", out var health))
            this.Health = health.Value;
        if (tag.TryGetTag<NbtTag<float>>("AbsorptionAmount", out var absorption))
            this.AbsorbtionAmount = (int)absorption.Value;

        this.PersistenceRequired = tag.TryGetBool("PersistenceRequired", out var persistent) && persistent;
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
