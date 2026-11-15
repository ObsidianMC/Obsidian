namespace Obsidian.API;

/// <summary>The vanilla potion effects. Each value is one more than the effect's registry id.</summary>
public enum PotionEffect : int
{
    /// <summary>The entity moves faster.</summary>
    Speed = 1,
    /// <summary>The entity moves slower.</summary>
    Slowness,
    /// <summary>The entity mines and attacks faster.</summary>
    Haste,
    /// <summary>The entity mines and attacks slower.</summary>
    MiningFatigue,
    /// <summary>The entity deals more melee damage.</summary>
    Strength,
    /// <summary>Heals the entity at once, or harms it if it is undead.</summary>
    InstantHealth,
    /// <summary>Harms the entity at once, or heals it if it is undead.</summary>
    InstantDamage,
    /// <summary>The entity jumps higher and takes less fall damage.</summary>
    JumpBoost,
    /// <summary>Makes the player's view wobble.</summary>
    Nausea,
    /// <summary>The entity heals over time.</summary>
    Regeneration,
    /// <summary>The entity takes less damage.</summary>
    Resistance,
    /// <summary>The entity takes no damage from fire and lava.</summary>
    FireResistance,
    /// <summary>The entity does not run out of air underwater.</summary>
    WaterBreathing,
    /// <summary>The entity is invisible.</summary>
    Invisibility,
    /// <summary>The entity sees only a short distance and cannot sprint.</summary>
    Blindness,
    /// <summary>The entity sees in the dark.</summary>
    NightVision,
    /// <summary>The entity gets hungry faster.</summary>
    Hunger,
    /// <summary>The entity deals less melee damage.</summary>
    Weakness,
    /// <summary>The entity takes damage over time, but not below half a heart.</summary>
    Poison,
    /// <summary>The entity takes damage over time and can die from it.</summary>
    Wither,
    /// <summary>The entity has more maximum health.</summary>
    HealthBoost,
    /// <summary>Gives the entity extra health that is lost before its normal health.</summary>
    Absorption,
    /// <summary>Restores the player's hunger.</summary>
    Saturation,
    /// <summary>The entity has an outline visible through blocks.</summary>
    Glowing,
    /// <summary>The entity floats upward.</summary>
    Levitation,
    /// <summary>Improves the entity's loot table rolls.</summary>
    Luck,
    /// <summary>Worsens the entity's loot table rolls.</summary>
    BadLuck,
    /// <summary>The entity falls slowly and takes no fall damage.</summary>
    SlowFalling,
    /// <summary>Lets the entity breathe, see and mine better underwater; given by conduits.</summary>
    ConduitPower,
    /// <summary>The entity swims faster.</summary>
    DolphinsGrace,
    /// <summary>Becomes a raid omen when the player enters a village, or a trial omen in a trial chamber.</summary>
    BadOmen,
    /// <summary>Gives the player villager discounts; given for defending a village from a raid.</summary>
    HeroOfTheVillage,
    /// <summary>Makes the player's view darken in pulses; given by wardens and sculk shriekers.</summary>
    Darkness,
    /// <summary>Turns trial spawners near the player ominous.</summary>
    TrialOmen,
    /// <summary>Starts a raid where the player is when the effect ends.</summary>
    RaidOmen,
    /// <summary>The entity bursts like a wind charge on death.</summary>
    WindCharged,
    /// <summary>The entity leaves cobwebs on death.</summary>
    Weaving,
    /// <summary>The entity drops slimes on death.</summary>
    Oozing,
    /// <summary>The entity sometimes releases silverfish when hurt.</summary>
    Infested,
    /// <summary>The entity does not lose air underwater.</summary>
    BreathOfTheNautilus
}
