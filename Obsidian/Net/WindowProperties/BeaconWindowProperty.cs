namespace Obsidian.Net.WindowProperties;

public class BeaconWindowProperty : IWindowProperty
{
    public short Property { get; }

    public short Value { get; }

    public BeaconWindowProperty(BeaconProperty property, short value)
    {
        Property = (short)property;
        Value = value;
    }
}

public enum BeaconProperty
{
    PowerLevel,

    FirstPotionEffect,
    SecondPotionEffect,
}
