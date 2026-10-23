namespace Obsidian.API.Entities;

/// <summary>
/// Vanilla's mob categories: which natural spawning pass spawns a mob, and whose cap it counts against.
/// </summary>
public enum MobCategory
{
    Monster,
    Creature,
    Ambient,
    Axolotls,
    UndergroundWaterCreature,
    WaterCreature,
    WaterAmbient,

    /// <summary>
    /// Not spawned naturally, like vanilla's villagers and golems.
    /// </summary>
    Misc
}
