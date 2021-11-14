namespace Obsidian.Net.WindowProperties;

public class LecternWindowProperty : IWindowProperty
{
    public short Property { get; }

    public short Value { get; }

    public LecternWindowProperty(short pageNumber)
    {
        this.Property = 0;
        this.Value = pageNumber;
    }
}
