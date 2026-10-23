using System.Runtime.InteropServices;

namespace Obsidian.API;

public sealed record class FireworkExplosion : INetworkSerializable<FireworkExplosion>
{
    public required int Shape { get; init; }

    public required ImmutableArray<int> Colors { get; init; }

    public required ImmutableArray<int> FadeColors { get; init; }

    public bool HasTrail { get; init; }

    public bool HasTwinkle { get; init; }

    public static FireworkExplosion Read(INetStreamReader reader) => new()
    {
        Shape = reader.ReadVarInt(),
        Colors = ImmutableCollectionsMarshal.AsImmutableArray(reader.ReadLengthPrefixedArray(reader.ReadVarInt)),
        FadeColors = ImmutableCollectionsMarshal.AsImmutableArray(reader.ReadLengthPrefixedArray(reader.ReadVarInt)),
        HasTrail = reader.ReadBoolean(),
        HasTwinkle = reader.ReadBoolean()
    };

    public static void Write(FireworkExplosion value, INetStreamWriter writer)
    {
        writer.WriteVarInt(value.Shape);

        writer.WriteLengthPrefixedArray(writer.WriteVarInt, value.Colors.AsSpan());
        writer.WriteLengthPrefixedArray(writer.WriteVarInt, value.FadeColors.AsSpan());

        writer.WriteBoolean(value.HasTrail);
        writer.WriteBoolean(value.HasTwinkle);
    }
}
