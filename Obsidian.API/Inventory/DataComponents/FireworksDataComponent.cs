using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API.Inventory.DataComponents;
public sealed record class FireworksDataComponent : IDataComponent
{
    public DataComponentType Type => DataComponentType.Fireworks;

    public string Identifier => "minecraft:fireworks";

    public required int FlightDuration { get; set; }

    public required List<FireworkExplosion> Explosions { get; set; }

    [SetsRequiredMembers]
    internal FireworksDataComponent() { }

    public void Read(INetStreamReader reader)
    {
        this.FlightDuration = reader.ReadVarInt();
        this.Explosions = reader.ReadLengthPrefixedArray(() => FireworkExplosion.Read(reader));
    }
    public void Write(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.FlightDuration);
        writer.WriteLengthPrefixedArray((value) => FireworkExplosion.Write(value, writer), this.Explosions);
    }

}
