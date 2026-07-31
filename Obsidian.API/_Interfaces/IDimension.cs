namespace Obsidian.API;

public interface IDimension : ILevel
{
    public IWorld ParentWorld { get; }
}
