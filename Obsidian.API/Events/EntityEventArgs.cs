namespace Obsidian.API.Events;

/// <summary>
/// The base class for events about an entity. Cancelling one skips the server's own handling of it.
/// </summary>
public class EntityEventArgs : BaseMinecraftEventArgs, ICancellable
{
    /// <summary>
    /// The entity involved in this event.
    /// </summary>
    public IEntity Entity { get; }

    /// <inheritdoc />
    public bool IsCancelled { get; private set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityEventArgs"/> class.
    /// </summary>
    /// <param name="entity">The entity involved in the event.</param>
    /// <param name="server">The server the event takes place in.</param>
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
