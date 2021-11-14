namespace Obsidian.API.Events;

public class EntityInteractEventArgs : EntityEventArgs
{
    /// <summary>
    /// The player who interacted with the entity.
    /// </summary>
    public IPlayer Player { get; }

    /// <summary>
    /// True if the player is sneaking when this event is triggered.
    /// </summary>
    public bool Sneaking { get; }

    public VectorF? TargetPosition { get; set; }

    public Hand? Hand { get; set; }

    public EntityInteractEventArgs(IPlayer player, IEntity entity, IServer server, bool sneaking = false) : base(entity, server)
    {
        this.Player = player;
        this.Sneaking = sneaking;
    }

    public EntityInteractEventArgs(IPlayer player, IEntity entity, IServer server, Hand hand, VectorF targetPosition, bool sneaking = false) : base(entity, server)
    {
        this.Player = player;
        this.Sneaking = sneaking;
        this.Hand = hand;
        this.TargetPosition = targetPosition;
    }
}
