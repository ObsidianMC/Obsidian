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

    public IReadOnlyDictionary<PotionEffect, PotionEffectData> ActivePotionEffects => activePotionEffects.AsReadOnly();

    private readonly ConcurrentDictionary<PotionEffect, PotionEffectData> activePotionEffects;

    public Living()
    {
        activePotionEffects = new ConcurrentDictionary<PotionEffect, PotionEffectData>();
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

    public bool HasPotionEffect(PotionEffect potion)
    {
        return activePotionEffects.ContainsKey(potion);
    }

    public void ClearPotionEffects()
    {
        foreach (var (potion, _) in activePotionEffects)
        {
            RemovePotionEffect(potion);
        }
    }

    public void AddPotionEffect(PotionEffect potion, int duration, byte amplifier = 0, EntityEffect effect = EntityEffect.None)
    {
        this.PacketBroadcaster.QueuePacketToWorld(this.World, new UpdateMobEffectPacket(EntityId, (int)potion, duration)
        {
            Amplifier = amplifier,
            Flags = effect
        });

        var data = new PotionEffectData(duration, amplifier, (byte)effect);
        activePotionEffects.AddOrUpdate(potion, _ => data, (_, _) => data);
    }

    public void RemovePotionEffect(PotionEffect potion)
    {
        this.PacketBroadcaster.QueuePacketToWorld(this.World, new RemoveMobEffectPacket(EntityId, (int)potion));
        activePotionEffects.TryRemove(potion, out _);
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
