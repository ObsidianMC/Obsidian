using Obsidian.API.Inventory;

namespace Obsidian.API;

/// <summary>
/// A mob players ride and steer, like vanilla's <c>PlayerRideable</c>. A player boards it through
/// <see cref="IEntity.StartRiding"/>, or by interacting with it once it's ready, like a saddled pig.
/// </summary>
public interface IRideable : IEntity
{
    /// <summary>
    /// The player in the controlling seat, or null when nobody or a non-player sits there.
    /// </summary>
    public IPlayer? Rider { get; }
}

/// <summary>
/// A rideable mob its rider speeds up with an item on a stick, like vanilla's <c>ItemSteerable</c>: a pig with a
/// carrot on a stick, or a strider with a warped fungus on a stick.
/// </summary>
public interface IItemSteerable : IRideable
{
    /// <summary>
    /// The item the rider steers and boosts the mob with.
    /// </summary>
    public Item SteeringItem { get; }

    /// <summary>
    /// How much a boost wears the steering item.
    /// </summary>
    public int BoostItemDamage { get; }

    /// <summary>
    /// Speeds the mob up for a while.
    /// </summary>
    /// <returns>Whether <paramref name="rider"/> is its rider and it wasn't speeding already.</returns>
    public bool Boost(IPlayer rider);
}

/// <summary>
/// A rideable mob its rider makes leap, like vanilla's <c>PlayerRideableJumping</c>: a camel's or nautilus's dash.
/// </summary>
public interface IRideableJumping : IRideable
{
    /// <summary>
    /// Asks for a leap on the mob's next tick, with the charge from 0 to 100 that the rider built up holding jump.
    /// </summary>
    /// <returns>Whether <paramref name="rider"/> is its rider and the mob can leap.</returns>
    public bool RequestJump(IPlayer rider, int charge);
}
