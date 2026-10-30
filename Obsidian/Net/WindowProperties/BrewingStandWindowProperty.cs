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

// Vanilla's BrewingStandBlockEntity data slots (26.3).
public enum BrewingStandProperty : short
{
    BrewTime,
    TotalBrewTime,
    TotalFuel,
    Fuel
}
