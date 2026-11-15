namespace Obsidian.API.Events;

/// <summary>
/// A player using (right-clicking) an entity within reach. Cancelling it skips the entity's response and the held
/// item's use on it.
/// </summary>
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

    /// <summary>
    /// Where on the entity the player clicked, relative to the entity's position, when a client interacted.
    /// </summary>
    public VectorF? TargetPosition { get; set; }

    /// <summary>
    /// Gets or sets the hand the player interacted with, or <see langword="null"/> if none was given.
    /// </summary>
    public InteractionHand? Hand { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityInteractEventArgs"/> class with no hand or target position.
    /// </summary>
    /// <param name="player">The player who interacted.</param>
    /// <param name="entity">The entity interacted with.</param>
    /// <param name="server">The server the event takes place in.</param>
    /// <param name="sneaking">Whether the player was sneaking.</param>
    public EntityInteractEventArgs(IPlayer player, IEntity entity, IServer server, bool sneaking = false) : base(entity, server)
    {
        this.Player = player;
        this.Sneaking = sneaking;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityInteractEventArgs"/> class for a client's click at a point on
    /// the entity.
    /// </summary>
    /// <param name="player">The player who interacted.</param>
    /// <param name="entity">The entity interacted with.</param>
    /// <param name="server">The server the event takes place in.</param>
    /// <param name="hand">The hand the player interacted with.</param>
    /// <param name="targetPosition">Where on the entity the player clicked, relative to its position.</param>
    /// <param name="sneaking">Whether the player was sneaking.</param>
    public EntityInteractEventArgs(IPlayer player, IEntity entity, IServer server, InteractionHand hand, VectorF targetPosition,
        bool sneaking = false) : base(entity, server)
    {
        this.Player = player;
        this.Sneaking = sneaking;
        this.Hand = hand;
        this.TargetPosition = targetPosition;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityInteractEventArgs"/> class with a hand but no target position.
    /// </summary>
    /// <param name="player">The player who interacted.</param>
    /// <param name="entity">The entity interacted with.</param>
    /// <param name="server">The server the event takes place in.</param>
    /// <param name="hand">The hand the player interacted with.</param>
    /// <param name="sneaking">Whether the player was sneaking.</param>
    public EntityInteractEventArgs(IPlayer player, IEntity entity, IServer server, InteractionHand hand, bool sneaking = false)
        : this(player, entity, server, sneaking)
    {
        this.Hand = hand;
    }
}
