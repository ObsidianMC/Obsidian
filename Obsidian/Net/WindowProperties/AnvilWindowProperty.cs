namespace Obsidian.Net.WindowProperties;

public class AnvilWindowProperty : IWindowProperty
{
    public short Property { get; }

    public short Value { get; }

    public AnvilWindowProperty(short repairCost)
    {
        Property = 0;
        Value = repairCost;
    }
}
