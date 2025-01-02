using Obsidian.API.Effects;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.Entities;

public class Living : Entity, ILiving
{
    public LivingBitMask LivingBitMask { get; set; }

    public uint ActiveEffectColor { get; private set; }

    public bool AmbientPotionEffect { get; set; }

    public int AbsorbedArrows { get; set; }

    public int AbsorbtionAmount { get; set; }

    public int AbsorbedStingers { get; set; }

    public Vector BedBlockPosition { get; set; }

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
        this.PacketBroadcaster.QueuePacketToWorld(this.World, new UpdateMobEffectPacket(EntityId, effectId, duration)
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
        this.PacketBroadcaster.QueuePacketToWorld(this.World, new RemoveMobEffectPacket(EntityId, effectId));
        activePotionEffects.TryRemove(effectId, out _);
    }
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteEntityMetadataType(8, EntityMetadataType.Byte);
        writer.WriteByte((byte)LivingBitMask);

        writer.WriteEntityMetadataType(9, EntityMetadataType.Float);
        writer.WriteFloat(Health);

        writer.WriteEntityMetadataType(10, EntityMetadataType.Particles);//This is a list of integers?
        writer.WriteVarInt(0);

        writer.WriteEntityMetadataType(11, EntityMetadataType.Boolean);
        writer.WriteBoolean(AmbientPotionEffect);

        writer.WriteEntityMetadataType(12, EntityMetadataType.VarInt);
        writer.WriteVarInt(AbsorbedArrows);

        writer.WriteEntityMetadataType(13, EntityMetadataType.VarInt);
        writer.WriteVarInt(AbsorbedStingers);

        writer.WriteEntityMetadataType(14, EntityMetadataType.OptionalBlockPos);
        writer.WriteBoolean(BedBlockPosition != default);
        if (BedBlockPosition != default)
            writer.WritePositionF(BedBlockPosition);
    }
}
