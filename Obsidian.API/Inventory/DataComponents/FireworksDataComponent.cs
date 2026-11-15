using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API.Inventory.DataComponents;

/// <summary>
/// A firework rocket's flight and explosions (<c>minecraft:fireworks</c>).
/// </summary>
public sealed record class FireworksDataComponent : DataComponent
{
    private ComponentValueKey Comparison => new(new object?[]
    {
        this.flightDuration,
        ComponentValueKey.Sequence(this.explosions)
    });

    /// <inheritdoc/>
    public bool Equals(FireworksDataComponent? other) => ReferenceEquals(this, other)
        || other is not null && base.Equals(other) && this.Comparison.Equals(other.Comparison);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(base.GetHashCode(), this.Comparison);

    /// <inheritdoc/>
    public override DataComponentType Type => DataComponentType.Fireworks;

    /// <inheritdoc/>
    public override string Identifier => "minecraft:fireworks";

    private int flightDuration;

    /// <summary>
    /// Gets the rocket's flight duration, the gunpowder it was crafted with; a longer duration flies longer.
    /// </summary>
    public required int FlightDuration
    {
        get => this.flightDuration;
        init => this.flightDuration = value;
    }

    private ImmutableArray<FireworkExplosion> explosions;

    /// <summary>
    /// Gets the explosions the rocket makes when it bursts.
    /// </summary>
    public required ImmutableArray<FireworkExplosion> Explosions
    {
        get => this.explosions;
        init => this.explosions = value;
    }

    [SetsRequiredMembers]
    internal FireworksDataComponent() { }

    /// <inheritdoc/>
    protected override void ReadCore(INetStreamReader reader)
    {
        this.flightDuration = reader.ReadVarInt();
        this.explosions = reader.ReadLengthPrefixedArray(() => ComponentValueCodecs.ReadExplosion(reader))
            .ToImmutableArray();
    }

    /// <inheritdoc/>
    public override void Write(INetStreamWriter writer)
    {
        writer.WriteVarInt(this.FlightDuration);
        writer.WriteLengthPrefixedArray((value) => ComponentValueCodecs.WriteExplosion(value, writer), this.Explosions.AsSpan());
    }
}
