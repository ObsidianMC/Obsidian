using Obsidian.Nbt;

namespace Obsidian.API.Inventory.DataComponents;

/// <summary>
/// The saved data of a mob caught in a bucket (<c>minecraft:bucket_entity_data</c>), which it's released with.
/// </summary>
public sealed record BucketEntityDataComponent()
    : SimpleDataComponent(DataComponentType.BucketEntityData, "minecraft:bucket_entity_data")
{
    private ComponentValueKey Comparison => new(new object?[]
    {
        this.value
    });

    /// <inheritdoc/>
    public bool Equals(BucketEntityDataComponent? other) => ReferenceEquals(this, other)
        || other is not null && base.Equals(other) && this.Comparison.Equals(other.Comparison);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(base.GetHashCode(), this.Comparison);

    private NbtCompound value = new();

    /// <summary>
    /// Gets the mob's saved data; a copy on each get and set.
    /// </summary>
    public NbtCompound Value
    {
        get => ComponentCopies.Nbt(this.value)!;
        init => this.value = ComponentCopies.Nbt(value)!;
    }

    /// <inheritdoc/>
    public override void Write(INetStreamWriter writer) => writer.WriteNbtCompound(this.value);

    /// <inheritdoc/>
    protected override void ReadCore(INetStreamReader reader) => this.value = reader.ReadNbtCompound();
}
