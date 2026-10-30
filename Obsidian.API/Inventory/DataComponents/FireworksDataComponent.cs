using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API.Inventory.DataComponents;

public sealed record class FireworksDataComponent : DataComponent
{
    private ComponentValueKey Comparison => new(new object?[]
    {
        this.flightDuration,
        ComponentValueKey.Sequence(this.explosions)
    });

    public bool Equals(FireworksDataComponent? other) => ReferenceEquals(this, other)
        || other is not null && base.Equals(other) && this.Comparison.Equals(other.Comparison);

    public override int GetHashCode() =>
        HashCode.Combine(base.GetHashCode(), this.Comparison);

    public override DataComponentType Type => DataComponentType.Fireworks;

    public override string Identifier => "minecraft:fireworks";

    private int flightDuration;

    public required int FlightDuration
    {
        get => this.flightDuration;
        init => this.flightDuration = value;
    }

    private ImmutableArray<FireworkExplosion> explosions;

    public required ImmutableArray<FireworkExplosion> Explosions
    {
        get => this.explosions;
        init => this.explosions = value;
    }

    [SetsRequiredMembers]
    internal FireworksDataComponent() { }

    protected override void ReadCore(INetStreamReader reader)
    {
        this.flightDuration = reader.ReadVarInt();
        this.explosions = reader.ReadLengthPrefixedArray(() => ComponentValueCodecs.ReadExplosion(reader))
            .ToImmutableArray();
    }

    public override void Write(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.FlightDuration);
        writer.WriteLengthPrefixedArray((value) => ComponentValueCodecs.WriteExplosion(value, writer), this.Explosions.AsSpan());
    }
}
