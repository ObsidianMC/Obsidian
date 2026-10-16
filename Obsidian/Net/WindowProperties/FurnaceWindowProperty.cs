namespace Obsidian.Net.WindowProperties;

public sealed class FurnaceWindowProperty : IWindowProperty
{
    public short Property { get; }

    public short Value { get; }

    public FurnaceWindowProperty(FurnaceProperty property, short value)
    {
        this.Property = (short)property;

        this.Value = value;
    }
}

public enum FurnaceProperty : short
{
    FuelLeft,

    MaximumFuelBurnTime,

    ProgressArrow,

    MaximumProgress
}
