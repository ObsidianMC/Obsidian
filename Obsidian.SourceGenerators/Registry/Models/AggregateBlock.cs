namespace Obsidian.SourceGenerators.Registry.Models;

internal sealed class AggregateBlock
{
    public string Name { get; }
    public BlockProperty Property { get; }
    public Block[] Targets { get; }

    private AggregateBlock(string name, BlockProperty property, Block[] targets)
    {
        Name = name;
        Property = property;
        Targets = targets;
    }

    public static AggregateBlock Create(string name, BlockProperty property, Block[] targets)
    {
        return new AggregateBlock(name, property, targets);
    }
}
