namespace Obsidian.API.AI;

public interface INavigator
{
    public bool IsNavigating { get; }
    public bool IsPaused { get; }

    public TargetType TargetType { get; }

    public VectorF TargetLocation { get; set; }

    public IEntity? Target { get; set; }

    public void NavigateTo(VectorF to);

    public void NavigateTo(IEntity to);
}

public enum TargetType
{
    Entity,

    Location
}
