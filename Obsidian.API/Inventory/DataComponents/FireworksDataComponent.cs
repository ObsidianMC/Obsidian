using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API.Inventory.DataComponents;
public sealed record class FireworksDataComponent : DataComponent
{
    public override DataComponentType Type => DataComponentType.Fireworks;

    public override string Identifier => "minecraft:fireworks";

    public required int FlightDuration { get; set; }

    public required FireworkExplosion[] Explosions { get; set; }

    [SetsRequiredMembers]
    internal FireworksDataComponent() { }

    public override void Read(INetStreamReader reader)
    {
        this.FlightDuration = reader.ReadVarInt();
        this.Explosions = reader.ReadLengthPrefixedArray(() => FireworkExplosion.Read(reader));
    }
    public override void Write(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.FlightDuration);
        writer.WriteLengthPrefixedArray((value) => FireworkExplosion.Write(value, writer), this.Explosions);
    }
}
