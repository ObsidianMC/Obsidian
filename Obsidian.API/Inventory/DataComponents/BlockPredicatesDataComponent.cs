using Obsidian.Nbt;

namespace Obsidian.API.Inventory.DataComponents;

/// <summary>
/// The blocks an item can break (<see cref="CanBreakDataComponent"/>) or be placed on
/// (<see cref="CanPlaceOnDataComponent"/>) in adventure mode; a block matching any predicate qualifies.
/// </summary>
public abstract record class BlockPredicatesDataComponent : DataComponent
{
    private ComponentValueKey Comparison => new(new object?[]
    {
        ComponentValueKey.Sequence(this.predicates),
        this.ShowInTooltip
    });

    /// <inheritdoc/>
    public virtual bool Equals(BlockPredicatesDataComponent? other) => ReferenceEquals(this, other)
        || other is not null && base.Equals(other) && this.Comparison.Equals(other.Comparison);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(base.GetHashCode(), this.Comparison);

    /// <inheritdoc/>
    public override DataComponentType Type { get; }

    /// <inheritdoc/>
    public override string Identifier { get; }

    private ImmutableArray<BlockPredicate> predicates = [];

    /// <summary>
    /// Gets the predicates; a block matching any of them qualifies.
    /// </summary>
    public ImmutableArray<BlockPredicate> Predicates
    {
        get => this.predicates;
        init => this.predicates = value;
    }

    /// <summary>
    /// Gets whether the tooltip lists the blocks. Neither sent to clients nor saved.
    /// </summary>
    public bool ShowInTooltip { get; init; }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public bool Equals(ComponentPredicate other) => this.Comparison.Equals(other.Comparison);

    /// <inheritdoc/>
    public override int GetHashCode() => this.Comparison.GetHashCode();

    /// <summary>
    /// Gets whether <see cref="TypeId"/> is a component predicate type rather than a component type.
    /// </summary>
    public required bool IsPredicateType { get; init; }
    /// <summary>
    /// Gets the id of the component predicate type, or of the data component type, as <see cref="IsPredicateType"/>
    /// says.
    /// </summary>
    public required int TypeId { get; init; }
    /// <summary>
    /// Gets the predicate's value as NBT; a copy on each get.
    /// </summary>
    public required NbtCompound Value
    {
        get => ComponentCopies.Nbt(field)!;
        init => field = ComponentCopies.Nbt(value)!;
    }
}

/// <summary>
/// One test of an adventure mode block predicate: a block passes when it matches every part that's set.
/// </summary>
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

    /// <inheritdoc/>
    public bool Equals(BlockPredicate other) => this.Comparison.Equals(other.Comparison);

    /// <inheritdoc/>
    public override int GetHashCode() => this.Comparison.GetHashCode();

    /// <summary>
    /// Creates a predicate with no parts set, which any block matches.
    /// </summary>
    public BlockPredicate() { }
    /// <summary>
    /// Gets the blocks that match, by registry id or tag, or null for any block.
    /// </summary>
    public IdSet? Blocks { get; init; }
    /// <summary>
    /// Gets components the block entity must have with exactly these values.
    /// </summary>
    public ImmutableArray<DataComponent> ExactComponents { get; init; } = [];
    /// <summary>
    /// Gets component predicates the block entity must pass.
    /// </summary>
    public ImmutableArray<ComponentPredicate> PartialComponents { get; init; } = [];
    /// <summary>
    /// Gets whether the predicate names blocks, by <see cref="Blocks"/> or <see cref="BlockIds"/>.
    /// </summary>
    public bool HasBlocks => this.Blocks.HasValue || this.BlockIds.Length > 0;

    /// <summary>
    /// Gets block identifiers read from a saved stack, used for saving it again when <see cref="Blocks"/> isn't set.
    /// </summary>
    public ImmutableArray<string> BlockIds { get; init; } = [];

    /// <summary>
    /// Gets whether the predicate tests block state properties.
    /// </summary>
    public bool HasProperties => this.Properties?.Length > 0;

    /// <summary>
    /// Gets the block state properties that must match, or null for none.
    /// </summary>
    public ImmutableArray<BlockProperty>? Properties { get; init; }

    /// <summary>
    /// Gets whether the predicate tests block entity NBT.
    /// </summary>
    public bool HasNbt => this.Nbt is not null;

    /// <summary>
    /// Gets NBT the block entity must contain, or null for none; a copy on each get.
    /// </summary>
    public NbtCompound? Nbt
    {
        get => ComponentCopies.Nbt(field);
        init => field = ComponentCopies.Nbt(value);
    }
}

/// <summary>
/// A block state property test of a <see cref="BlockPredicate"/>: an exact value or a range.
/// </summary>
public readonly record struct BlockProperty
{
    /// <summary>
    /// Gets the property's name, like <c>facing</c>.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets whether the property must equal <see cref="ExactValue"/> rather than lie between <see cref="MinValue"/> and
    /// <see cref="MaxValue"/>.
    /// </summary>
    public required bool IsExactMatch { get; init; }

    /// <summary>
    /// Gets the value the property must have, when <see cref="IsExactMatch"/> is set.
    /// </summary>
    public string? ExactValue { get; init; }
    /// <summary>
    /// Gets the lowest value the property may have, or null for no lower bound.
    /// </summary>
    public string? MinValue { get; init; }
    /// <summary>
    /// Gets the highest value the property may have, or null for no upper bound.
    /// </summary>
    public string? MaxValue { get; init; }
}
