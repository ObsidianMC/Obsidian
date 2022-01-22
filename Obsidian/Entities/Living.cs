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

    public Vector BedBlockPosition { get; set; }

    public IDictionary<PotionEffect, PotionEffectData> ActivePotionEffects => _activePotionEffects;

    private readonly ConcurrentDictionary<PotionEffect, PotionEffectData> _activePotionEffects;

    public Living()
    {
        _activePotionEffects = new ConcurrentDictionary<PotionEffect, PotionEffectData>();
    }

    public async override Task TickAsync()
    {
        foreach (var (potion, data) in _activePotionEffects)
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
        return _activePotionEffects.ContainsKey(potion);
    }

    public async Task ClearPotionEffects()
    {
        foreach (var (potion, _) in _activePotionEffects)
        {
            await RemovePotionEffectAsync(potion);
        }
    }
    
    public Task AddPotionEffectAsync(PotionEffect potion, int duration) => AddPotionEffectAsync(potion, duration, 0);

    public Task AddPotionEffectAsync(PotionEffect potion, int duration, byte amplifier) =>
        AddPotionEffectAsync(potion, duration, amplifier, true, true, false);

    public async Task AddPotionEffectAsync(PotionEffect potion, int duration, byte amplifier, bool showParticles,
        bool showIcon, bool isAmbient)
    {
        byte flags = 0;
        if (isAmbient)
            flags |= 0x01;
        if (showParticles)
            flags |= 0x02;
        if (showIcon)
            flags |= 0x04;

        await this.server.QueueBroadcastPacketAsync(new EntityEffectPacket(EntityId, (byte) potion, duration)
        {
            Amplifier = amplifier,
            Flags = flags
        });

        var data = new PotionEffectData(duration, amplifier, flags);
        _activePotionEffects.AddOrUpdate(potion, _ => data, (_, _) => data);
    }

    public async Task RemovePotionEffectAsync(PotionEffect potion)
    {
        await this.server.QueueBroadcastPacketAsync(new RemoveEntityEffectPacket(EntityId, (byte) potion));
        _activePotionEffects.TryRemove(potion, out _);
    }


    public bool Alive => this.Health > 0f;

    public override async Task WriteAsync(MinecraftStream stream)
    {
        await base.WriteAsync(stream);

        await stream.WriteEntityMetdata(7, EntityMetadataType.Byte, (byte)this.LivingBitMask);

        await stream.WriteEntityMetdata(8, EntityMetadataType.Float, this.Health);

        await stream.WriteEntityMetdata(9, EntityMetadataType.VarInt, (int)this.ActiveEffectColor);

        await stream.WriteEntityMetdata(10, EntityMetadataType.Boolean, this.AmbientPotionEffect);

        await stream.WriteEntityMetdata(11, EntityMetadataType.VarInt, this.AbsorbedArrows);

        await stream.WriteEntityMetdata(12, EntityMetadataType.VarInt, this.AbsorbtionAmount);

        await stream.WriteEntityMetdata(13, EntityMetadataType.OptPosition, this.BedBlockPosition, this.BedBlockPosition != API.Vector.Zero);
    }

    public override void Write(MinecraftStream stream)
    {
        base.Write(stream);

        stream.WriteEntityMetadataType(7, EntityMetadataType.Byte);
        stream.WriteByte((byte)LivingBitMask);

        stream.WriteEntityMetadataType(8, EntityMetadataType.Float);
        stream.WriteFloat(Health);

        stream.WriteEntityMetadataType(9, EntityMetadataType.VarInt);
        stream.WriteVarInt((int)ActiveEffectColor);

        stream.WriteEntityMetadataType(10, EntityMetadataType.Boolean);
        stream.WriteBoolean(AmbientPotionEffect);

        stream.WriteEntityMetadataType(11, EntityMetadataType.VarInt);
        stream.WriteVarInt(AbsorbedArrows);

        stream.WriteEntityMetadataType(12, EntityMetadataType.VarInt);
        stream.WriteVarInt(AbsorbtionAmount);

        stream.WriteEntityMetadataType(13, EntityMetadataType.OptPosition);
        stream.WriteBoolean(BedBlockPosition != default);
        if (BedBlockPosition != default)
            stream.WritePositionF(BedBlockPosition);
    }
}
