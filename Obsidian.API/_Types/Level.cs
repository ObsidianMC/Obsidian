namespace Obsidian.API;

/// <summary>
/// https://minecraft.wiki/w/Java_Edition_level_format
/// </summary>
public sealed class Level
{
    public const int DataVersion = 3337;

    /// <summary>
    /// 1 or 0 (true/false) - true if cheats are enabled.
    /// </summary>
    public bool AllowCommands { get; set; }

    /// <summary>
    /// Center of the world border on the X coordinate. Defaults to 0.
    /// </summary>
    public double BorderCenterX { get; set; }

    /// <summary>
    /// Center of the world border on the Z coordinate. Defaults to 0.
    /// </summary>
    public double BorderCenterZ { get; set; }

    /// <summary>
    ///  Defaults to 0.2.
    /// </summary>
    public double BorderDamagePerBlock { get; set; } = 0.2;

    /// <summary>
    /// Width and length of the border of the border. Defaults to 60000000.
    /// </summary>
    public double BorderSize { get; set; } = 60000000;

    /// <summary>
    /// Defaults to 5.
    /// </summary>
    public double BorderSafeZone { get; set; } = 5;

    /// <summary>
    /// Defaults to 60000000.
    /// </summary>
    public double BorderSizeLerpTarget { get; set; } = 60000000;

    /// <summary>
    /// Defaults to 0.
    /// </summary>
    public long BorderSizeLerpTime { get; set; }

    /// <summary>
    /// Defaults to 5.
    /// </summary>
    public double BorderWarningBlocks { get; set; } = 5;

    /// <summary>
    /// Defaults to 15.
    /// </summary>
    public double BorderWarningTime { get; set; } = 15;

    /// <summary>
    /// The number of ticks until "clear weather" has ended.
    /// </summary>
    public int ClearWeatherTime { get; set; }

    /// <summary>
    /// The time of day. 0 is sunrise, 6000 is mid day, 12000 is sunset, 18000 is mid night, 24000 is the next day's 0.
    /// </summary>
    public int DayTime
    {
        get
        {
            return (int)(this.Time % 24000); // day time is based on server time
        }
        set
        {
            var startOfDay = this.Time - (this.Time % 24000);
            this.Time = startOfDay + value;
        }
    }

    /// <summary>
    /// The current difficulty
    /// </summary>
    public Difficulty Difficulty { get; set; }

    /// <summary>
    /// True if the difficulty has been locked. Defaults to 0.
    /// </summary>
    public bool DifficultyLocked { get; set; }


    /// <summary>
    /// The default game mode for the singleplayer player when they initially spawn.
    /// </summary>
    public Gamemode DefaultGamemode { get; set; }

    /// <summary>
    /// Used in 1.15 and below. The name of the world generator.
    /// </summary>
    public string GeneratorName { get; set; }

    /// <summary>
    /// Used in 1.15 and below. Used in buffet, superflat and old customized worlds.
    /// </summary>
    public object GeneratorOptions { get; set; }

    /// <summary>
    /// Used in 1.15 and below. The version of the level generator.
    /// </summary>
    public int GeneratorVersion { get; set; }

    /// <summary>
    /// true if the player will respawn in Spectator on death in singleplayer. Affects all three game modes.
    /// </summary>
    public bool Hardcore { get; set; }

    /// <summary>
    /// Normally true after a world has been initialized properly after creation. 
    /// If the initial simulation was canceled somehow, this can be false and the world will be re-initialized on next load.
    /// </summary>
    public bool Initialized { get; set; }

    /// <summary>
    /// The Unix time in milliseconds when the level was last loaded.
    /// </summary>
    public long LastPlayed { get; set; }

    public string LevelName { get; set; }

    /// <summary>
    /// true if the map generator should place structures such as villages, strongholds, and mineshafts. 
    /// Defaults to 1. Always 1 if the world type is Customized.
    /// </summary>
    public bool MapFeatures { get; set; }

    /// <summary>
    /// true if the level is currently experiencing rain, snow, and cloud cover.
    /// </summary>
    public bool Raining { get; set; } = false; // start a world without rain

    /// <summary>
    /// The number of ticks before "raining" is toggled and this value gets set to another random value.
    /// </summary>
    public int RainTime { get; set; }

    /// <summary>
    /// The random level seed used to generate consistent terrain.
    /// </summary>
    public long RandomSeed { get; set; }

    public VectorF SpawnPosition { get; set; }

    /// <summary>
    /// true if the rain/snow/cloud cover is a lightning storm and dark enough for mobs to spawn under the sky.
    /// </summary>
    public bool Thundering { get; set; }

    /// <summary>
    /// The number of ticks before "thundering" is toggled and this value gets set to another random value.
    /// </summary>
    public int ThunderTime { get; set; }

    /// <summary>
    /// The number of ticks since the start of the level.
    /// </summary>
    public long Time { get; set; }

    /// <summary>
    /// The NBT version of the level
    /// </summary>
    public int Version { get; set; } = 19133;

    /// <summary>
    /// The UUID of the current wandering trader in the world saved as four ints.
    /// </summary>
    public Guid WanderingTraderId { get; set; }

    /// <summary>
    /// The current chance of the wandering trader spawning next attempt; 
    /// this value is the percentage and will be divided by 10 when loaded by the game, 
    /// for example a value of 50 means 5.0% chance.
    /// </summary>
    public int WanderingTraderSpawnChance { get; set; }

    /// <summary>
    /// The amount of ticks until another wandering trader is attempted to spawn
    /// </summary>
    public int WanderingTraderSpawnDelay { get; set; }
}
