using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Obsidian.API.Inventory.DataComponents;
public sealed record class FireworksDataComponent : DataComponent
{
    public override DataComponentType Type => DataComponentType.Fireworks;

    public override string Identifier => "minecraft:fireworks";

    public required int FlightDuration { get; set; }

    public required ImmutableArray<FireworkExplosion> Explosions { get; set; }

    [SetsRequiredMembers]
    internal FireworksDataComponent() { }

    public override void Read(INetStreamReader reader)
    {
        this.FlightDuration = reader.ReadVarInt();
        this.Explosions = ImmutableCollectionsMarshal.AsImmutableArray(reader.ReadLengthPrefixedArray(() => ComponentValueCodecs.ReadExplosion(reader)));
    }
    public override void Write(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.FlightDuration);
        writer.WriteLengthPrefixedArray((value) => ComponentValueCodecs.WriteExplosion(value, writer), this.Explosions.AsSpan());
    }
}
