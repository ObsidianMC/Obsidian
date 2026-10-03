using System.Runtime.InteropServices;

namespace Obsidian.API;

public sealed record class FireworkExplosion : INetworkSerializable<FireworkExplosion>
{
    public required int Shape { get; set; }

    public required ImmutableArray<int> Colors { get; set; }

    public required ImmutableArray<int> FadeColors { get; set; }

    public bool HasTrail { get; set; }

    public bool HasTwinkle { get; set; }

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
