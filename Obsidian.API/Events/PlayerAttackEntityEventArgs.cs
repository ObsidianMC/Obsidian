namespace Obsidian.API.Events;

//TODO check if player crits and calculate damage
public sealed class PlayerAttackEntityEventArgs : BaseEventArgs, IEntityEvent, IPlayerEvent
{
    internal PlayerAttackEntityEventArgs(IPlayer attacker, IEntity entity, bool sneaking)
    {
        Player = attacker;
        Entity = entity;
        Sneaking = sneaking;
    }

    public float Damage { get; internal set; }

    public bool IsCritical { get; internal set; }

    /// <summary>
    /// True if the player is sneaking when this event is triggered.
    /// </summary>
    public bool Sneaking { get; }

    /// <summary>
    /// The entity that was attacked.
    /// </summary>
    public IEntity Entity { get; }

    /// <summary>
    /// The attacker.
    /// </summary>
    public IPlayer Player { get; }
}
