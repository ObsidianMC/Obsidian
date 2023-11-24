namespace Obsidian.Net.WindowProperties;

public sealed class LecternWindowProperty : IWindowProperty
{
    public short Property { get; }

    public short Value { get; }

    public LecternWindowProperty(short pageNumber)
    {
        this.Property = 0;
        this.Value = pageNumber;
    }
}
