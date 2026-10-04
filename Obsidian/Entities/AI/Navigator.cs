using Obsidian.API.AI;

namespace Obsidian.Entities.AI;

public class Navigator : INavigator
{
    public bool IsNavigating { get; set; }

    public bool IsPaused { get; set; }

    public TargetType TargetType { get; set; }

    public VectorD TargetLocation { get; set; }
    public IEntity? Target { get; set; }

    public void NavigateTo(VectorD to)
    {

    }

    public void NavigateTo(IEntity to)
    {

    }
}
