namespace Obsidian.Net.WindowProperties;

/// <summary>
/// One of a menu's data slots, by index, like vanilla's <c>ContainerData</c>: a crafter's slot states, say.
/// </summary>
public sealed class ContainerDataProperty(short property, short value) : IWindowProperty
{
    public short Property { get; } = property;

    public short Value { get; } = value;
}
