using Obsidian.API._Types;
using Obsidian.Net;
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

    public async override Task TickAsync()
    {
        foreach (var (potion, data) in activePotionEffects)
        {
            data.CurrentDuration--;

            if (data.CurrentDuration <= 0)
            {
                await RemovePotionEffectAsync(potion);
            }
        }
    }

    public bool HasPotionEffect(PotionEffect potion)
    {
        return activePotionEffects.ContainsKey(potion);
    }

    public async Task ClearPotionEffects()
    {
        foreach (var (potion, _) in activePotionEffects)
        {
            await RemovePotionEffectAsync(potion);
        }
    }

    public async Task AddPotionEffectAsync(PotionEffect potion, int duration, byte amplifier = 0, bool showParticles = true,
        bool showIcon = true, bool isAmbient = false)
    {
        byte flags = 0;
        if (isAmbient)
            flags |= 0x01;
        if (showParticles)
            flags |= 0x02;
        if (showIcon)
            flags |= 0x04;

        await this.server.QueueBroadcastPacketAsync(new EntityEffectPacket(EntityId, (int)potion, duration)
        {
            Amplifier = amplifier,
            Flags = flags
        });

        var data = new PotionEffectData(duration, amplifier, flags);
        activePotionEffects.AddOrUpdate(potion, _ => data, (_, _) => data);
    }

    public async Task RemovePotionEffectAsync(PotionEffect potion)
    {
        await this.server.QueueBroadcastPacketAsync(new RemoveEntityEffectPacket(EntityId, (int)potion));
        activePotionEffects.TryRemove(potion, out _);
    }
    

    public override async Task WriteAsync(MinecraftStream stream)
    {
        await base.WriteAsync(stream);

        await stream.WriteEntityMetdata(8, EntityMetadataType.Byte, (byte)this.LivingBitMask);

        await stream.WriteEntityMetdata(9, EntityMetadataType.Float, this.Health);

        await stream.WriteEntityMetdata(10, EntityMetadataType.VarInt, (int)this.ActiveEffectColor);

        await stream.WriteEntityMetdata(11, EntityMetadataType.Boolean, this.AmbientPotionEffect);

        await stream.WriteEntityMetdata(12, EntityMetadataType.VarInt, this.AbsorbedArrows);

        await stream.WriteEntityMetdata(13, EntityMetadataType.VarInt, this.AbsorbedStingers);

        await stream.WriteEntityMetdata(14, EntityMetadataType.OptPosition, this.BedBlockPosition, this.BedBlockPosition != Vector.Zero);
    }

    public override void Write(MinecraftStream stream)
    {
        base.Write(stream);

        stream.WriteEntityMetadataType(8, EntityMetadataType.Byte);
        stream.WriteByte((byte)LivingBitMask);

        stream.WriteEntityMetadataType(9, EntityMetadataType.Float);
        stream.WriteFloat(Health);

        stream.WriteEntityMetadataType(10, EntityMetadataType.VarInt);
        stream.WriteVarInt((int)ActiveEffectColor);

        stream.WriteEntityMetadataType(11, EntityMetadataType.Boolean);
        stream.WriteBoolean(AmbientPotionEffect);

        stream.WriteEntityMetadataType(12, EntityMetadataType.VarInt);
        stream.WriteVarInt(AbsorbedArrows);

        stream.WriteEntityMetadataType(13, EntityMetadataType.VarInt);
        stream.WriteVarInt(AbsorbedStingers);

        stream.WriteEntityMetadataType(14, EntityMetadataType.OptPosition);
        stream.WriteBoolean(BedBlockPosition != default);
        if (BedBlockPosition != default)
            stream.WritePositionF(BedBlockPosition);
    }
}
