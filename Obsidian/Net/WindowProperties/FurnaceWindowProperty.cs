namespace Obsidian.Net.WindowProperties;

public class FurnaceWindowProperty : IWindowProperty
{
    public short Property { get; }

    public short Value { get; }

    public FurnaceWindowProperty(FurnaceProperty property, short value)
    {
        this.Property = (short)property;

        if (property == FurnaceProperty.ProgressArrow && value > 200)
            throw new InvalidOperationException();

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
