using Obsidian.Nbt;

namespace Obsidian.API.Inventory.DataComponents;

public sealed record BucketEntityDataComponent()
    : SimpleDataComponent(DataComponentType.BucketEntityData, "minecraft:bucket_entity_data")
{
    private ComponentValueKey Comparison => new(new object?[]
    {
        this.value
    });

    public bool Equals(BucketEntityDataComponent? other) => ReferenceEquals(this, other)
        || other is not null && base.Equals(other) && this.Comparison.Equals(other.Comparison);

    public override int GetHashCode() =>
        HashCode.Combine(base.GetHashCode(), this.Comparison);

    private NbtCompound value = new();

    public NbtCompound Value
    {
        get => ComponentCopies.Nbt(this.value)!;
        init => this.value = ComponentCopies.Nbt(value)!;
    }

    public override void Write(INetStreamWriter writer) => writer.WriteNbtCompound(this.value);

    protected override void ReadCore(INetStreamReader reader) => this.value = reader.ReadNbtCompound();
}
