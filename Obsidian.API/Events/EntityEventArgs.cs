namespace Obsidian.API.Events;

public class EntityEventArgs : BaseMinecraftEventArgs, ICancellable
{
    public override string Name => "EntityEvent";

    /// <summary>
    /// The entity involved in this event.
    /// </summary>
    public IEntity Entity { get; }

    /// <inheritdoc />
    public bool IsCancelled { get; private set; }

    public EntityEventArgs(IEntity entity, IServer server) : base(server)
    {
        this.Entity = entity;
    }

    /// <inheritdoc />
    public void Cancel()
    {
        IsCancelled = true;
    }
}
