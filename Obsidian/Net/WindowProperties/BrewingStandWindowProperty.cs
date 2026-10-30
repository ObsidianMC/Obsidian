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

// Vanilla's BrewingStandBlockEntity data slots (26.3), whose ids BrewingStandMenu's getters read. 26.3 added the
// totals after the two older slots rather than beside them.
public enum BrewingStandProperty : short
{
    BrewTime = 0,
    Fuel = 1,
    TotalBrewTime = 2,
    TotalFuel = 3
}
