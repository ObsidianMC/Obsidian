namespace Obsidian.Net.WindowProperties;

public sealed class AnvilWindowProperty : IWindowProperty
{
    public short Property { get; }

    public short Value { get; }

    public AnvilWindowProperty(short repairCost)
    {
        this.Property = 0;
        this.Value = repairCost;
    }
}
