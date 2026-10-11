using Obsidian.Nbt;

namespace Obsidian.API.Inventory.DataComponents;
public abstract record class BlockPredicatesDataComponent : DataComponent
{
    public override DataComponentType Type { get; }

    public override string Identifier { get; }

    public List<BlockPredicate> Predicates { get; set; } = [];

    public bool ShowInTooltip { get; init; }

    public override void Read(INetStreamReader reader)
    {
        this.Predicates = reader.ReadLengthPrefixedArray(() => new BlockPredicate
        {
            Blocks = reader.ReadBoolean() ? reader.ReadIdSet() : null,
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
            }).ToList() : null,
            Nbt = reader.ReadBoolean() ? reader.ReadNbtCompound() : null,
            ExactComponents = reader.ReadLengthPrefixedArray(() => reader.ReadDataComponent(reader.ReadVarInt<DataComponentType>())),
            PartialComponents = reader.ReadLengthPrefixedArray(() => new ComponentPredicate
            {
                IsPredicateType = reader.ReadBoolean(),
                TypeId = reader.ReadVarInt(),
                Value = reader.ReadNbtCompound()
            })
        }).ToList();
    }

    public override void Write(INetStreamWriter writer)
    {
        writer.WriteLengthPrefixedArray(predicate =>
        {
            writer.WriteBoolean(predicate.Blocks.HasValue);
            if (predicate.Blocks.HasValue)
                IdSet.Write(predicate.Blocks.Value, writer);

            writer.WriteBoolean(predicate.Properties is not null);
            if (predicate.Properties is not null)
            {
                writer.WriteLengthPrefixedArray(property =>
                {
                    writer.WriteString(property.Name);
                    writer.WriteBoolean(property.IsExactMatch);
                    if (property.IsExactMatch)
                        writer.WriteString(property.ExactValue!);
                    else
                    {
                        writer.WriteOptional(property.MinValue);
                        writer.WriteOptional(property.MaxValue);
                    }
                }, predicate.Properties.ToArray());
            }

            writer.WriteBoolean(predicate.Nbt is not null);
            if (predicate.Nbt is not null)
                writer.WriteNbtCompound(predicate.Nbt);

            writer.WriteLengthPrefixedArray(component =>
            {
                writer.WriteVarInt(component.Type);
                writer.WriteDataComponent(component);
            }, predicate.ExactComponents);
            writer.WriteLengthPrefixedArray(component =>
            {
                writer.WriteBoolean(component.IsPredicateType);
                writer.WriteVarInt(component.TypeId);
                writer.WriteNbtCompound(component.Value);
            }, predicate.PartialComponents);
        }, this.Predicates.ToArray());
    }
}


public readonly record struct ComponentPredicate
{
    public required bool IsPredicateType { get; init; }
    public required int TypeId { get; init; }
    public required NbtCompound Value { get; init; }
}

public readonly record struct BlockPredicate
{
    public BlockPredicate() { }
    public IdSet? Blocks { get; init; }
    public DataComponent[] ExactComponents { get; init; } = [];
    public ComponentPredicate[] PartialComponents { get; init; } = [];
    public bool HasBlocks => this.Blocks.HasValue || this.BlockIds.Count > 0;

    public List<string> BlockIds { get; init; } = [];

    public bool HasProperties => this.Properties?.Count > 0;

    public List<BlockProperty>? Properties { get; init; }

    public bool HasNbt => this.Nbt is not null;

    public NbtCompound? Nbt { get; init; }
}

public readonly record struct BlockProperty
{
    public required string Name { get; init; }

    public required bool IsExactMatch { get; init; }

    public string? ExactValue { get; init; }
    public string? MinValue { get; init; }
    public string? MaxValue { get; init; }
}
