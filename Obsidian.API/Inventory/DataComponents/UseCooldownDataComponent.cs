using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API.Inventory.DataComponents;

public sealed record class UseCooldownDataComponent : DataComponent
{
    private ComponentValueKey Comparison => new(new object?[]
    {
        this.seconds,
        this.cooldownGroup
    });

    public bool Equals(UseCooldownDataComponent? other) => ReferenceEquals(this, other)
        || other is not null && base.Equals(other) && this.Comparison.Equals(other.Comparison);

    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), this.Comparison);

    public override DataComponentType Type => DataComponentType.UseCooldown;

    public override string Identifier => "minecraft:use_cooldown";

    private float seconds;

    public required float Seconds
    {
        get => this.seconds;
        init => this.seconds = value;
    }

    private string? cooldownGroup;

    public string? CooldownGroup
    {
        get => this.cooldownGroup;
        init => this.cooldownGroup = value;
    }

    [SetsRequiredMembers]
    internal UseCooldownDataComponent() { }

    protected override void ReadCore(INetStreamReader reader)
    {
        this.seconds = reader.ReadSingle();
        this.cooldownGroup = reader.ReadOptionalString();
    }

    public override void Write(INetStreamWriter writer)
    {
        writer.WriteSingle(this.Seconds);
        writer.WriteOptional(this.CooldownGroup);
    }
}
