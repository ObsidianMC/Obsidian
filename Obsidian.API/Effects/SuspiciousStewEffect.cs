using Obsidian.API.Inventory.DataComponents;

namespace Obsidian.API.Effects;
/// <summary>A potion effect that a bowl of suspicious stew gives when eaten.</summary>
public sealed class SuspiciousStewEffect : INetworkSerializable<SuspiciousStewEffect>
{
    /// <summary>
    /// Gets the effect's zero-based mob effect registry id, which is one less than its <see cref="PotionEffect"/>
    /// value.
    /// </summary>
    public required int EffectId { get; init; }

    /// <summary>Gets how long the effect lasts, in ticks.</summary>
    public required int Duration { get; init; }

    /// <summary>Reads an effect from a network stream.</summary>
    public static SuspiciousStewEffect Read(INetStreamReader reader) => new()
    {
        EffectId = reader.ReadRegistryId(ComponentValueCodecs.MobEffects),
        Duration = reader.ReadVarInt()
    };

    /// <summary>Writes an effect to a network stream.</summary>
    public static void Write(SuspiciousStewEffect value, INetStreamWriter writer)
    {
        writer.WriteRegistryId(ComponentValueCodecs.MobEffects, value.EffectId);
        writer.WriteVarInt(value.Duration);
    }
}
