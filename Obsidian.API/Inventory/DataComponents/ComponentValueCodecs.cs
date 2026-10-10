using System.Runtime.InteropServices;

namespace Obsidian.API.Inventory.DataComponents;

// Correct 1.21.11 component encodings for shared API value types also used by older serializers.
internal static class ComponentValueCodecs
{
    public static SoundEvent ReadSoundHolder(INetStreamReader reader)
    {
        var id = reader.ReadVarInt();
        return id == 0 ? reader.ReadSoundEvent() : new SoundEvent { RegistryId = id - 1, ResourceLocation = string.Empty };
    }

    public static void WriteSoundHolder(SoundEvent sound, INetStreamWriter writer)
    {
        writer.WriteVarInt(sound.RegistryId is int id ? id + 1 : 0);
        if (sound.RegistryId is null)
            writer.WriteSoundEvent(sound);
    }

    public static FireworkExplosion ReadExplosion(INetStreamReader reader) => new()
    {
        Shape = reader.ReadVarInt(),
        Colors = ImmutableCollectionsMarshal.AsImmutableArray(reader.ReadLengthPrefixedArray(reader.ReadInt)),
        FadeColors = ImmutableCollectionsMarshal.AsImmutableArray(reader.ReadLengthPrefixedArray(reader.ReadInt)),
        HasTrail = reader.ReadBoolean(),
        HasTwinkle = reader.ReadBoolean()
    };

    public static void WriteExplosion(FireworkExplosion value, INetStreamWriter writer)
    {
        writer.WriteVarInt(value.Shape);
        writer.WriteLengthPrefixedArray(writer.WriteInt, value.Colors.AsSpan());
        writer.WriteLengthPrefixedArray(writer.WriteInt, value.FadeColors.AsSpan());
        writer.WriteBoolean(value.HasTrail);
        writer.WriteBoolean(value.HasTwinkle);
    }

    public static PotionEffectData ReadEffect(INetStreamReader reader) => ReadEffectDetails(reader.ReadVarInt(), reader);

    private static PotionEffectData ReadEffectDetails(int id, INetStreamReader reader) => new()
    {
        Id = id,
        Amplifier = reader.ReadVarInt(),
        Duration = reader.ReadVarInt(),
        Ambient = reader.ReadBoolean(),
        ShowParticles = reader.ReadBoolean(),
        ShowIcon = reader.ReadBoolean(),
        HiddenEffect = reader.ReadBoolean() ? ReadEffectDetails(id, reader) : null
    };

    public static void WriteEffect(PotionEffectData value, INetStreamWriter writer)
    {
        writer.WriteVarInt(value.Id);
        WriteEffectDetails(value, writer);
    }

    private static void WriteEffectDetails(PotionEffectData value, INetStreamWriter writer)
    {
        writer.WriteVarInt(value.Amplifier);
        writer.WriteVarInt(value.Duration);
        writer.WriteBoolean(value.Ambient);
        writer.WriteBoolean(value.ShowParticles);
        writer.WriteBoolean(value.ShowIcon);
        writer.WriteBoolean(value.HiddenEffect is not null);
        if (value.HiddenEffect is not null)
            WriteEffectDetails(value.HiddenEffect, writer);
    }
}
