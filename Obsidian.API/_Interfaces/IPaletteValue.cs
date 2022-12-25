namespace Obsidian.API;

public interface IPaletteValue<TSelf> where TSelf : IPaletteValue<TSelf>
{
    public static abstract TSelf Construct(int value);
}
