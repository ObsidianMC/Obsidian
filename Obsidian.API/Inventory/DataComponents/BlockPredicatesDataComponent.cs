using Obsidian.Nbt;

namespace Obsidian.API.Inventory.DataComponents;
public abstract record class BlockPredicatesDataComponent : DataComponent
{
    public override DataComponentType Type { get; }

    public override string Identifier { get; }

    public List<BlockPredicate> Predicates { get; init; }

    public bool ShowInTooltip { get; init; }

    public void WriteHashed(INetStreamWriter writer) => throw new NotImplementedException();
}


public readonly record struct BlockPredicate
{
    public bool HasBlocks => this.BlockIds.Count > 0;

    public List<string> BlockIds { get; init; }

    public bool HasProperties => this.Properties.Count > 0;

    public List<BlockProperty> Properties { get; init; }

    public bool HasNbt => this.Nbt != null;

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
