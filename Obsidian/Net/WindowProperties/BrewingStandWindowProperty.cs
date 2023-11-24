namespace Obsidian.Net.WindowProperties;

public sealed class BrewingStandWindowProperty : IWindowProperty
{
    public short Property { get; }

    public short Value { get; }

    public BrewingStandWindowProperty(BrewingStandProperty property, short value)
    {
        this.Property = (short)property;
        this.Value = value;
    }
}

public enum BrewingStandProperty : short
{
    BrewTime,
    FuelTime
}
