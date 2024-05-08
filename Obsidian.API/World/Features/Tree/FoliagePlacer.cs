namespace Obsidian.API.World.Features.Tree;
public abstract class FoliagePlacer
{
    public abstract string Type { get; init; }
    public virtual IIntProvider Radius { get; set; } = default!;
    public virtual IIntProvider Offset { get; set; } = default!;

    public virtual int GetHeight(int treeHeight) => 0;

    public virtual int GetRadius(int rootHeight) => this.Radius.Get();
}
