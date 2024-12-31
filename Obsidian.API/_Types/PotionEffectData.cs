using System.Runtime.CompilerServices;

namespace Obsidian.API;

/// <summary>
/// The potion effect data holds additional information about an active <see cref="PotionEffect"/>.
/// </summary>
public sealed record class PotionEffectData : INetworkSerializable<PotionEffectData>
{
    public required int Id { get; init; }

    /// <summary>
    /// The amplifier of the potion effect.
    /// </summary>
    public required int Amplifier { get; init; }

    /// <summary>
    /// The duration of the potion effect when it was added.
    /// </summary>
    public required int Duration { get; init; }

    /// <summary>
    /// Produces more translucent particle effects if true.
    /// </summary>
    public bool Ambient { get; init; }


    /// <summary>
    /// Completely hides effect particles if false.
    /// </summary>
    public bool ShowParticles { get; init; }

    /// <summary>
    /// Shows the potion icon in the inventory screen if true.
    /// </summary>
    public bool ShowIcon { get; init; }

    /// <summary>
    /// Used to store the state of the previous potion effect when a stronger one is applied. 
    /// This guarantees that the weaker one will persist, in case it lasts longer.
    /// </summary>
    public PotionEffectData? HiddenEffect { get; init; }

    public static PotionEffectData Read(INetStreamReader reader) => new()
    {
        Id = reader.ReadVarInt(),
        Amplifier = reader.ReadVarInt(),
        Duration = reader.ReadVarInt(),

        Ambient = reader.ReadBoolean(),
        ShowParticles = reader.ReadBoolean(),
        ShowIcon = reader.ReadBoolean(),

        HiddenEffect = reader.ReadOptional<PotionEffectData>()
    };

    public static void Write(PotionEffectData value, INetStreamWriter writer)
    {
        writer.WriteVarInt(value.Id);
        writer.WriteVarInt(value.Amplifier);
        writer.WriteVarInt(value.Duration);

        writer.WriteBoolean(value.Ambient);
        writer.WriteBoolean(value.ShowParticles);
        writer.WriteBoolean(value.ShowIcon);

        writer.WriteOptional(value.HiddenEffect);
    }
}
