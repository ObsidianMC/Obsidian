using System.Runtime.InteropServices;

namespace Obsidian.API.Inventory.DataComponents;

// Correct 26.3 component encodings for shared API value types also used by older serializers.
internal static class ComponentValueCodecs
{
    // The built-in registries whose ids components hold, which a connection may number differently
    // (INetStreamReader.ReadRegistryId).
    public const string Attributes = "minecraft:attribute";
    public const string BlockEntityTypes = "minecraft:block_entity_type";
    public const string Blocks = "minecraft:block";
    public const string DataComponentTypes = "minecraft:data_component_type";
    public const string EntityTypes = "minecraft:entity_type";
    public const string Items = "minecraft:item";
    public const string MobEffects = "minecraft:mob_effect";
    public const string SoundEvents = "minecraft:sound_event";

    public static SoundEvent ReadSoundHolder(INetStreamReader reader) =>
        reader.ReadRegistryHolder(SoundEvents) is int id
            ? new SoundEvent { RegistryId = id, ResourceLocation = string.Empty }
            : reader.ReadSoundEvent();

    public static void WriteSoundHolder(SoundEvent sound, INetStreamWriter writer)
    {
        writer.WriteRegistryHolder(SoundEvents, sound.RegistryId);
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

    public static PotionEffectData ReadEffect(INetStreamReader reader) =>
        ReadEffectDetails(reader.ReadRegistryId(MobEffects), reader);

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
        writer.WriteRegistryId(MobEffects, value.Id);
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
