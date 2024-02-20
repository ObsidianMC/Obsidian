using System.ComponentModel.DataAnnotations;

namespace Obsidian.API.World.Features.Tree;
public abstract class DecoratorBase
{
    public abstract string Type { get; init; }

    [Range(0.0, 1.0)]
    public virtual float Probability { get; set; }
}
