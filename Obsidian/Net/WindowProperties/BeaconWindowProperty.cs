namespace Obsidian.Net.WindowProperties;

public sealed class BeaconWindowProperty : IWindowProperty
{
    public short Property { get; }

    public short Value { get; }

    public BeaconWindowProperty(BeaconProperty property, short value)
    {
        this.Property = (short)property;
        this.Value = value;
    }
}

public enum BeaconProperty
{
    PowerLevel,

    FirstPotionEffect,
    SecondPotionEffect,
}
