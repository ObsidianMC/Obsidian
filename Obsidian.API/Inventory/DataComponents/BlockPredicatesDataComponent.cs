using Obsidian.Nbt;

namespace Obsidian.API.Inventory.DataComponents;

public abstract record class BlockPredicatesDataComponent : DataComponent
{
    private ComponentValueKey Comparison => new(new object?[]
    {
        ComponentValueKey.Sequence(this.predicates),
        this.ShowInTooltip
    });

    public virtual bool Equals(BlockPredicatesDataComponent? other) => ReferenceEquals(this, other)
        || other is not null && base.Equals(other) && this.Comparison.Equals(other.Comparison);

    public override int GetHashCode() =>
        HashCode.Combine(base.GetHashCode(), this.Comparison);

    public override DataComponentType Type { get; }

    public override string Identifier { get; }

    private ImmutableArray<BlockPredicate> predicates = [];

    public ImmutableArray<BlockPredicate> Predicates
    {
        get => this.predicates;
        init => this.predicates = value;
    }

    public bool ShowInTooltip { get; init; }

    protected override void ReadCore(INetStreamReader reader)
    {
        this.predicates = reader.ReadLengthPrefixedArray(() => new BlockPredicate
        {
            Blocks = reader.ReadBoolean() ? reader.ReadIdSet(ComponentValueCodecs.Blocks) : null,
            Properties = reader.ReadBoolean() ? reader.ReadLengthPrefixedArray(() =>
            {
                var name = reader.ReadString();
                var exact = reader.ReadBoolean();
                return exact
                    ? new BlockProperty { Name = name, IsExactMatch = true, ExactValue = reader.ReadString() }
                    : new BlockProperty
                    {
                        Name = name,
                        IsExactMatch = false,
                        MinValue = reader.ReadOptionalString(),
                        MaxValue = reader.ReadOptionalString()
                    };
            }).ToImmutableArray() : null,
            Nbt = reader.ReadBoolean() ? reader.ReadNbtCompound() : null,
            ExactComponents = reader.ReadLengthPrefixedArray(() =>
            {
                var type = (DataComponentType)reader.ReadRegistryId(ComponentValueCodecs.DataComponentTypes);
                return reader.ReadDataComponent(type);
            }).ToImmutableArray(),
            PartialComponents = reader.ReadLengthPrefixedArray(() =>
            {
                var isPredicateType = reader.ReadBoolean();
                return new ComponentPredicate
                {
                    IsPredicateType = isPredicateType,
                    TypeId = isPredicateType
                        ? reader.ReadVarInt()
                        : reader.ReadRegistryId(ComponentValueCodecs.DataComponentTypes),
                    Value = reader.ReadNbtCompound()
                };
            }).ToImmutableArray()
        }).ToImmutableArray();
    }

    public override void Write(INetStreamWriter writer)
    {
        writer.WriteLengthPrefixedArray(predicate =>
        {
            writer.WriteBoolean(predicate.Blocks.HasValue);
            if (predicate.Blocks.HasValue)
                IdSet.Write(predicate.Blocks.Value, writer, ComponentValueCodecs.Blocks);

            writer.WriteBoolean(predicate.Properties is not null);
            if (predicate.Properties is not null)
            {
                writer.WriteLengthPrefixedArray(property =>
                {
                    writer.WriteString(property.Name);
                    writer.WriteBoolean(property.IsExactMatch);
                    if (property.IsExactMatch)
                    {
                        writer.WriteString(property.ExactValue!);
                    }
                    else
                    {
                        writer.WriteOptional(property.MinValue);
                        writer.WriteOptional(property.MaxValue);
                    }
                }, predicate.Properties.Value.AsSpan());
            }

            writer.WriteBoolean(predicate.Nbt is not null);
            if (predicate.Nbt is not null)
                writer.WriteNbtCompound(predicate.Nbt);

            writer.WriteLengthPrefixedArray(component =>
            {
                writer.WriteRegistryId(ComponentValueCodecs.DataComponentTypes, (int)component.Type);
                writer.WriteDataComponent(component);
            }, predicate.ExactComponents.AsSpan());
            writer.WriteLengthPrefixedArray(component =>
            {
                writer.WriteBoolean(component.IsPredicateType);
                if (component.IsPredicateType)
                    writer.WriteVarInt(component.TypeId);
                else
                    writer.WriteRegistryId(ComponentValueCodecs.DataComponentTypes, component.TypeId);

                writer.WriteNbtCompound(component.Value);
            }, predicate.PartialComponents.AsSpan());
        }, this.Predicates.ToArray());
    }
}


/// <summary>
/// A partial component predicate: a <c>minecraft:data_component_predicate_type</c> id if
/// <see cref="IsPredicateType"/>, otherwise the id of the data component type that must be present.
/// </summary>
public readonly record struct ComponentPredicate
{
    private ComponentValueKey Comparison => new(new object?[]
    {
        this.IsPredicateType,
        this.TypeId,
        this.Value
    });

    public bool Equals(ComponentPredicate other) => this.Comparison.Equals(other.Comparison);

    public override int GetHashCode() => this.Comparison.GetHashCode();

    public required bool IsPredicateType { get; init; }
    public required int TypeId { get; init; }
    public required NbtCompound Value
    {
        get => ComponentCopies.Nbt(field)!;
        init => field = ComponentCopies.Nbt(value)!;
    }
}

public readonly record struct BlockPredicate
{
    private ComponentValueKey Comparison => new(new object?[]
    {
        this.Blocks,
        ComponentValueKey.Sequence(this.ExactComponents),
        this.PartialComponents.IsDefaultOrEmpty
            ? Enumerable.Empty<ComponentPredicate>()
            : this.PartialComponents.OrderBy(predicate => predicate.IsPredicateType)
                .ThenBy(predicate => predicate.TypeId),
        ComponentValueKey.Sequence(this.BlockIds),
        ComponentValueKey.Sequence(this.Properties),
        this.Nbt
    });

    public bool Equals(BlockPredicate other) => this.Comparison.Equals(other.Comparison);

    public override int GetHashCode() => this.Comparison.GetHashCode();

    public BlockPredicate() { }
    public IdSet? Blocks { get; init; }
    public ImmutableArray<DataComponent> ExactComponents { get; init; } = [];
    public ImmutableArray<ComponentPredicate> PartialComponents { get; init; } = [];
    public bool HasBlocks => this.Blocks.HasValue || this.BlockIds.Length > 0;

    public ImmutableArray<string> BlockIds { get; init; } = [];

    public bool HasProperties => this.Properties?.Length > 0;

    public ImmutableArray<BlockProperty>? Properties { get; init; }

    public bool HasNbt => this.Nbt is not null;

    public NbtCompound? Nbt
    {
        get => ComponentCopies.Nbt(field);
        init => field = ComponentCopies.Nbt(value);
    }
}

public readonly record struct BlockProperty
{
    public required string Name { get; init; }

    public required bool IsExactMatch { get; init; }

    public string? ExactValue { get; init; }
    public string? MinValue { get; init; }
    public string? MaxValue { get; init; }
}
