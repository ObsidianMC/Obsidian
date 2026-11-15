using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API.Inventory.DataComponents;

/// <summary>
/// A cooldown the item starts when used (<c>minecraft:use_cooldown</c>).
/// </summary>
public sealed record class UseCooldownDataComponent : DataComponent
{
    private ComponentValueKey Comparison => new(new object?[]
    {
        this.seconds,
        this.cooldownGroup
    });

    /// <inheritdoc/>
    public bool Equals(UseCooldownDataComponent? other) => ReferenceEquals(this, other)
        || other is not null && base.Equals(other) && this.Comparison.Equals(other.Comparison);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), this.Comparison);

    /// <inheritdoc/>
    public override DataComponentType Type => DataComponentType.UseCooldown;

    /// <inheritdoc/>
    public override string Identifier => "minecraft:use_cooldown";

    private float seconds;

    /// <summary>
    /// Gets the cooldown's length, in seconds.
    /// </summary>
    public required float Seconds
    {
        get => this.seconds;
        init => this.seconds = value;
    }

    private string? cooldownGroup;

    /// <summary>
    /// Gets the group sharing the cooldown, or null for the item's own.
    /// </summary>
    public string? CooldownGroup
    {
        get => this.cooldownGroup;
        init => this.cooldownGroup = value;
    }

    [SetsRequiredMembers]
    internal UseCooldownDataComponent() { }

    /// <inheritdoc/>
    protected override void ReadCore(INetStreamReader reader)
    {
        this.seconds = reader.ReadSingle();
        this.cooldownGroup = reader.ReadOptionalString();
    }

    /// <inheritdoc/>
    public override void Write(INetStreamWriter writer)
    {
        writer.WriteSingle(this.Seconds);
        writer.WriteOptional(this.CooldownGroup);
    }
}
