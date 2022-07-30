namespace Obsidian.API.Events;

public sealed class EntityInteractEventArgs : BaseEventArgs, IEntityEvent, ICancellable
{
    internal EntityInteractEventArgs(IPlayer player, IEntity entity, bool sneaking = false)
    {
        Player = player;
        Entity = entity;
        Sneaking = sneaking;
    }

    internal EntityInteractEventArgs(IPlayer player, IEntity entity, Hand hand, VectorF targetPosition, bool sneaking = false)
    {
        Player = player;
        Entity = entity;
        Sneaking = sneaking;
        Hand = hand;
        TargetPosition = targetPosition;
    }

    /// <summary>
    /// The player who interacted with the entity.
    /// </summary>
    public IPlayer Player { get; }

    /// <summary>
    /// True if the player is sneaking when this event is triggered.
    /// </summary>
    public bool Sneaking { get; }
    public VectorF? TargetPosition { get; }
    public Hand? Hand { get; }

    public IEntity Entity { get; }
    public bool Cancelled { get; set; }
}
