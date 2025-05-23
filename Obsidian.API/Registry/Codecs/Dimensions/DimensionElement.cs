using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;
using System.Text.Json.Serialization;

namespace Obsidian.API.Registry.Codecs.Dimensions;

public sealed record class DimensionElement : INbtSerializable
{
    public int MonsterSpawnBlockLightLimit { get; set; }

    public MonsterSpawnLightLevel MonsterSpawnLightLevel { get; set; }

    /// <summary>
    /// Whether piglins shake and transform to zombified piglins.
    /// </summary>
    public bool PiglinSafe { get; set; }

    /// <summary>
    /// Whether this dimension is considered natural.
    /// </summary>
    /// <remarks>
    /// When false, compasses spin randomly, and using a bed to set the respawn point or sleep, is disabled. When true, nether portals can spawn zombified piglins.
    /// </remarks>
    public bool Natural { get; set; }

    /// <summary>
    /// How much light the dimension has.
    /// </summary>
    /// <remarks>
    /// Can be 0.0 to 1.0.
    /// </remarks>
    public float AmbientLight { get; set; } = 0.0f;

    /// <summary>
    /// the time of the day is the specified value.
    /// </summary>
    /// <remarks>
    /// Can be anything from 0 to 24000. If the value is not set this will cause the world to have constant sunrise. 
    /// </remarks>
    public long? FixedTime { get; set; }

    /// <summary>
    /// A resource location defining what block tag to use for infiniburn.
    /// </summary>
    /// <remarks>
    /// If undefined, the value will be automatically set to "minecraft:infiniburn_overworld" .
    /// </remarks>
    public string Infiniburn { get; set; } = "minecraft:infiniburn_overworld";

    /// <summary>
    /// Whether players can charge and use respawn anchors.
    /// </summary>
    public bool RespawnAnchorWorks { get; set; }

    /// <summary>
    /// Whether the dimension has skylight access or not.
    /// </summary>
    public bool HasSkylight { get; set; }

    /// <summary>
    /// Whether players can use a bed to sleep.
    /// </summary>
    /// <remarks>
    /// When false, the bed blows up when trying to sleep.
    /// </remarks>
    public bool BedWorks { get; set; }

    /// <summary>
    /// Determines the dimension effect used for this dimension. 
    /// </summary>
    /// <remarks>
    /// Setting to overworld makes the dimension have clouds, sun, stars and moon. 
    /// Setting to the nether makes the dimension have thick fog blocking that sight, similar to the nether. 
    /// Setting to the end makes the dimension have dark spotted sky similar to the end, ignoring the sky and fog color. 
    /// If undefined, the value will be automatically set to "minecraft:overworld".
    /// </remarks>
    public string Effects { get; set; } = "minecraft:overworld";

    /// <summary>
    /// Whether players with the Bad Omen effect can cause a raid.
    /// </summary>
    public bool HasRaids { get; set; }

    /// <summary>
    /// The minimum height in which blocks can exist within this dimension. 
    /// </summary>
    /// <remarks>
    /// Should be between -2032 and 2031 and be a multiple of 16 (effectively making 2016 the maximum).
    /// Setting it lower than -2048 will only allow the temporary placement of blocks below it as they won't be saved. 
    /// Furthermore, lighting won't work correctly at Y-coordinate -2048 and below.
    /// </remarks>
    public int MinY { get; set; } = -64;

    /// <summary>
    /// The total height in which blocks can exist within this dimension.
    /// </summary>
    /// <remarks>
    /// Should be between 0 and 4064 and be a multiple of 16. 
    /// It can be set higher than the maximum by the same amount of temporary layers + 16. Max y = min y + height, and may not be more than 2032.
    /// </remarks>
    public int Height { get; set; } = 384;

    /// <summary>
    /// The maximum height to which chorus fruits and nether portals can bring players within this dimension. 
    /// </summary>
    /// <remarks> 
    /// This excludes portals that were already built above the limit as they still connect normally.
    /// May not be greater than height.
    /// </remarks>
    public int LogicalHeight { get; set; } = 384;

    /// <summary>
    ///  The multiplier applied to coordinates when leaving the dimension.
    /// </summary>
    public float CoordinateScale { get; set; } = 1.0f;

    /// <summary>
    /// Whether the dimensions behaves like the nether (water evaporates and sponges dry) or not.
    /// </summary>
    /// <remarks>
    ///  Also lets stalactites drip lava and causes lava to spread faster and thinner.
    /// </remarks>
    public bool Ultrawarm { get; set; }

    /// <summary>
    /// Whether the dimension has a bedrock ceiling or not.
    /// </summary>
    public bool HasCeiling { get; set; }

    public void Write(INbtWriter writer)
    {
        writer.WriteBool("piglin_safe", this.PiglinSafe);
        writer.WriteBool("natural", this.Natural);

        writer.WriteFloat("ambient_light", this.AmbientLight);

        if (this.FixedTime.HasValue)
            writer.WriteLong("fixed_time", this.FixedTime.Value);

        writer.WriteString("infiniburn", this.Infiniburn);

        writer.WriteBool("respawn_anchor_works", this.RespawnAnchorWorks);
        writer.WriteBool("has_skylight", this.HasSkylight);
        writer.WriteBool("bed_works", this.BedWorks);

        writer.WriteString("effects", this.Effects);

        writer.WriteBool("has_raids", this.HasRaids);

        writer.WriteInt("monster_spawn_block_light_limit", this.MonsterSpawnBlockLightLimit);

        if (this.MonsterSpawnLightLevel.IntValue.HasValue)
            writer.WriteInt("monster_spawn_light_level", this.MonsterSpawnLightLevel.IntValue.Value);
        else
        {
            var monsterLight = this.MonsterSpawnLightLevel.Value!.Value;
            writer.WriteTag(new NbtCompound("monster_spawn_light_level")
            {
                new NbtTag<string>("type", monsterLight.Type),
                new NbtTag<int>("max_inclusive", monsterLight.MaxInclusive),
                new NbtTag<int>("min_inclusive", monsterLight.MinInclusive)
            });
        }

        writer.WriteInt("min_y", this.MinY);

        writer.WriteInt("height", this.Height);

        writer.WriteInt("logical_height", this.LogicalHeight);

        writer.WriteFloat("coordinate_scale", this.CoordinateScale);

        writer.WriteBool("ultrawarm", this.Ultrawarm);
        writer.WriteBool("has_ceiling", this.HasCeiling);
    }
}

public sealed record class MonsterSpawnLightLevel
{
    public MonsterSpawnLightLevelValue? Value { get; set; }

    [JsonIgnore]
    public int? IntValue { get; set; }
}

public record struct MonsterSpawnLightLevelValue
{
    public int MaxInclusive { get; set; }

    public int MinInclusive { get; set; }

    public string? Type { get; set; }
}
