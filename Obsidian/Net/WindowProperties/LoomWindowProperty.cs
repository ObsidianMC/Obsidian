namespace Obsidian.Net.WindowProperties;

public class LoomWindowProperty : IWindowProperty
{
    public short Property { get; }

    public short Value { get; }

    public LoomWindowProperty(short selectedPatternIndex)
    {
        Property = 0;
        Value = selectedPatternIndex;
    }
}
