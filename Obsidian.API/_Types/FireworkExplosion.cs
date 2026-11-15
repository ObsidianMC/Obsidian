using System.Runtime.InteropServices;

namespace Obsidian.API;

/// <summary>One burst of a firework rocket or firework star.</summary>
public sealed record class FireworkExplosion : INetworkSerializable<FireworkExplosion>
{
    /// <summary>Gets the burst's shape, a <see cref="FireworkExplosionShape"/> value.</summary>
    public required int Shape { get; init; }

    /// <summary>Gets the colors of the burst's particles, as RGB values.</summary>
    public required ImmutableArray<int> Colors { get; init; }

    /// <summary>Gets the colors the particles fade to, as RGB values; empty for no fade.</summary>
    public required ImmutableArray<int> FadeColors { get; init; }

    /// <summary>Gets whether the particles leave a trail.</summary>
    public bool HasTrail { get; init; }

    /// <summary>Gets whether the particles twinkle.</summary>
    public bool HasTwinkle { get; init; }

    /// <summary>Reads a burst from a network stream.</summary>
    public static FireworkExplosion Read(INetStreamReader reader) => new()
    {
        Shape = reader.ReadVarInt(),
        Colors = ImmutableCollectionsMarshal.AsImmutableArray(reader.ReadLengthPrefixedArray(reader.ReadVarInt)),
        FadeColors = ImmutableCollectionsMarshal.AsImmutableArray(reader.ReadLengthPrefixedArray(reader.ReadVarInt)),
        HasTrail = reader.ReadBoolean(),
        HasTwinkle = reader.ReadBoolean()
    };

    /// <summary>Writes a burst to a network stream.</summary>
    public static void Write(FireworkExplosion value, INetStreamWriter writer)
    {
        writer.WriteVarInt(value.Shape);

        writer.WriteLengthPrefixedArray(writer.WriteVarInt, value.Colors.AsSpan());
        writer.WriteLengthPrefixedArray(writer.WriteVarInt, value.FadeColors.AsSpan());

        writer.WriteBoolean(value.HasTrail);
        writer.WriteBoolean(value.HasTwinkle);
    }
}
