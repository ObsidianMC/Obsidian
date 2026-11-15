namespace Obsidian.API.Entities;

/// <summary>
/// Vanilla's mob categories: which natural spawning pass spawns a mob, and whose cap it counts against.
/// </summary>
public enum MobCategory
{
    /// <summary>
    /// Hostile mobs, like zombies and skeletons.
    /// </summary>
    Monster,
    /// <summary>
    /// Passive land animals, like cows and sheep.
    /// </summary>
    Creature,
    /// <summary>
    /// Ambient mobs, like bats.
    /// </summary>
    Ambient,
    /// <summary>
    /// Axolotls, which have their own cap.
    /// </summary>
    Axolotls,
    /// <summary>
    /// Water mobs spawned underground, like glow squids.
    /// </summary>
    UndergroundWaterCreature,
    /// <summary>
    /// Water animals, like squid and dolphins.
    /// </summary>
    WaterCreature,
    /// <summary>
    /// Small water mobs, like fish.
    /// </summary>
    WaterAmbient,

    /// <summary>
    /// Not spawned naturally, like vanilla's villagers and golems.
    /// </summary>
    Misc
}
