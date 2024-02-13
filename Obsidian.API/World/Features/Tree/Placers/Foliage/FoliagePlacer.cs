using System.ComponentModel.DataAnnotations;

namespace Obsidian.API.World.Features.Tree.Placers.Foliage;
public abstract class FoliagePlacer
{
    public abstract string Type { get; }
    public virtual IIntProvider Radius { get; set; } = default!;
    public virtual IIntProvider Offset { get; set; } = default!;

    [Range(0, 16)]
    public virtual IIntProvider Height { get; set; }
}
