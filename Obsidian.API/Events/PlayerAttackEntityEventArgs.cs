namespace Obsidian.API.Events;

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

    public float Damage { get; internal set; }

    public bool IsCrit { get; internal set; }

    /// <summary>
    /// True if the player is sneaking when this event is triggered.
    /// </summary>
    public bool Sneaking { get; }

    public PlayerAttackEntityEventArgs(IPlayer attacker, IEntity entity, IServer server, bool sneaking) : base(entity, server)
    {
        this.Attacker = attacker;
        this.Sneaking = sneaking;
    }
}
