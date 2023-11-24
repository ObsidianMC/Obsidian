namespace Obsidian.Net.WindowProperties;

public sealed class StonecutterWindowProperty : IWindowProperty
{
    public short Property { get; }

    public short Value { get; }

    public StonecutterWindowProperty(short selectedRecipeIndex)
    {
        this.Property = 0;
        this.Value = selectedRecipeIndex;
    }
}
