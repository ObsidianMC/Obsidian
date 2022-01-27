namespace Obsidian.API.Events;

//TODO check if player crits and calculate damage
public class PlayerAttackEntityEventArgs : EntityEventArgs
{
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
        Attacker = attacker;
        Sneaking = sneaking;
    }
}
