namespace Obsidian.API.Events;

/// <summary>
/// A player attacking an entity. Cancelling it keeps the attack from dealing damage or knockback.
/// </summary>
public class PlayerAttackEntityEventArgs : EntityEventArgs
{
    internal Inventory.ItemStack? Weapon { get; init; }
    internal int WeaponSlot { get; init; } = -1;
    internal bool PiercingAttack { get; init; }
    internal bool ChargeAttack { get; init; }
    internal bool ChargeKnockback { get; init; }
    internal bool ChargeDismount { get; init; }
    /// <summary>
    /// The player who interacted with the entity.
    /// </summary>
    public IPlayer Attacker { get; }

    /// <summary>
    /// Gets the damage the server worked out for the attack when the event was raised, in half-hearts.
    /// </summary>
    public float Damage { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether the attack is a critical hit.
    /// </summary>
    public bool IsCrit { get; internal set; }

    /// <summary>
    /// True if the player is sneaking when this event is triggered.
    /// </summary>
    public bool Sneaking { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlayerAttackEntityEventArgs"/> class.
    /// </summary>
    /// <param name="attacker">The attacking player.</param>
    /// <param name="entity">The entity being attacked.</param>
    /// <param name="server">The server the event takes place in.</param>
    /// <param name="sneaking">Whether the attacker was sneaking.</param>
    public PlayerAttackEntityEventArgs(IPlayer attacker, IEntity entity, IServer server, bool sneaking) : base(entity, server)
    {
        this.Attacker = attacker;
        this.Sneaking = sneaking;
    }
}
