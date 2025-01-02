namespace Obsidian.API.Inventory.DataComponents;
public abstract class BlockPredicatesDataComponent : IDataComponent
{
    public abstract DataComponentType Type { get; }

    public abstract string Identifier { get; }

    public List<BlockPredicate> Predicates { get; init; }

    public bool ShowInTooltip { get; init; }

    public virtual void Read(INetStreamReader reader) => throw new NotImplementedException();
    public virtual void Write(INetStreamWriter writer) => throw new NotImplementedException();
}


public readonly struct BlockPredicate
{
    public bool HasBlocks => this.BlockIds.Count > 0;

    public List<string> BlockIds { get; init; }


    public bool HasProperties => this.Properties.Count > 0;

    public List<BlockProperty> Properties { get; init; }

    public bool HasNbt => this.Nbt != null;

    //WE NEED NBT PAIN
    public object? Nbt { get; init; }
}

public readonly struct BlockProperty
{
    public required string Name { get; init; }

    public required bool IsExactMatch { get; init; }

    public string? ExactValue { get; init; }
    public string? MinValue { get; init; }
    public string? MaxValue { get; init; }
}
