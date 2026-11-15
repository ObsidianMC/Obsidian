namespace Obsidian.API;

/// <summary>
/// A level that belongs to a world other than its overworld, like the Nether or the End.
/// </summary>
public interface IDimension : ILevel
{
    /// <summary>
    /// Gets the world the dimension belongs to.
    /// </summary>
    public IWorld ParentWorld { get; }
}
