namespace Obsidian.Net.WindowProperties;

public class BrewingStandWindowProperty : IWindowProperty
{
    public short Property { get; }

    public short Value { get; }

    public BrewingStandWindowProperty(BrewingStandProperty property, short value)
    {
        Property = (short)property;
        Value = value;
    }
}

public enum BrewingStandProperty : short
{
    BrewTime,
    FuelTime
}
