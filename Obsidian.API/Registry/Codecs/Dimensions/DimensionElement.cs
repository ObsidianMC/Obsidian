using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.Dimensions;

/// <summary>
/// The parts of a 1.21.11 dimension type the server uses. Its environment attributes, timelines, skybox and cardinal
/// light are only sent to clients, for plugins' entries.
/// </summary>
public sealed record class DimensionElement : INbtSerializable
{
    /// <summary>
    /// The block light level at or below which monsters can spawn, 0 to 15.
    /// </summary>
    public int MonsterSpawnBlockLightLimit { get; set; }

    /// <summary>
    /// The light level monsters can spawn at, 0 to 15.
    /// </summary>
    public MonsterSpawnLightLevel MonsterSpawnLightLevel { get; set; } = new() { IntValue = 0 };

    /// <summary>
    /// The monster spawn light level as the provider a plugin's type decodes to, which the server samples; null for
    /// vanilla's types and ones made in code, whose <see cref="MonsterSpawnLightLevel"/> is sampled. Not written:
    /// clients get <see cref="MonsterSpawnLightLevel"/>.
    /// </summary>
    public IIntProvider? MonsterSpawnLightProvider { get; set; }

    /// <summary>
    /// Whether piglins don't turn into zombified piglins. Not part of 1.21.11's dimension types (its
    /// <c>minecraft:gameplay/piglins_zombify</c> attribute), so false unless set in code.
    /// </summary>
    public bool PiglinSafe { get; set; }

    /// <summary>
    /// How much light the dimension has, 0.0 to 1.0.
    /// </summary>
    public float AmbientLight { get; set; }

    /// <summary>
    /// The fixed time of day. Not part of 1.21.11's dimension types, so null unless set in code.
    /// </summary>
    public long? FixedTime { get; set; }

    /// <summary>
    /// The block tag of blocks fire burns on forever, like <c>#minecraft:infiniburn_overworld</c>.
    /// </summary>
    public string Infiniburn { get; set; } = "#minecraft:infiniburn_overworld";

    /// <summary>
    /// Whether the dimension has sky light.
    /// </summary>
    public bool HasSkylight { get; set; }

    /// <summary>
    /// The lowest Y blocks can be at: a multiple of 16 from -2032 to 2016.
    /// </summary>
    public int MinY { get; set; } = -64;

    /// <summary>
    /// How many blocks high the dimension is: a multiple of 16 from 16 to 4064, with <see cref="MinY"/> + height at
    /// most 2032.
    /// </summary>
    public int Height { get; set; } = 384;

    /// <summary>
    /// The highest chorus fruits and nether portals can take players, at most <see cref="Height"/>.
    /// </summary>
    public int LogicalHeight { get; set; } = 384;

    /// <summary>
    /// The multiplier applied to coordinates when leaving the dimension, 0.00001 to 30,000,000.
    /// </summary>
    public double CoordinateScale { get; set; } = 1.0;

    /// <summary>
    /// Whether water evaporates, sponges dry and lava flows faster. Not part of 1.21.11's dimension types (its
    /// <c>minecraft:gameplay/water_evaporates</c> attribute), so false unless set in code.
    /// </summary>
    public bool Ultrawarm { get; set; }

    /// <summary>
    /// Whether the dimension has a bedrock ceiling.
    /// </summary>
    public bool HasCeiling { get; set; }

    /// <summary>
    /// The tag of a plugin's dimension type's timelines, like <c>#minecraft:in_overworld</c>, or null for none or for
    /// vanilla's types. The server reads it for the day cycle. It isn't written: clients get it from the type's data.
    /// </summary>
    public string? Timelines { get; set; }

    /// <summary>
    /// Whether undead monsters burn in daylight at any time of day: the type's own
    /// <c>minecraft:gameplay/monsters_burn</c> attribute, which the overworld's day timeline otherwise sets by day.
    /// Not written, like <see cref="Timelines"/>.
    /// </summary>
    public bool MonstersBurn { get; set; }

    /// <summary>
    /// The type's own <c>minecraft:gameplay/sky_light_level</c> attribute applied to its default of 15, before the
    /// day and the weather change it; null for vanilla's types, which the server knows. Not written, like
    /// <see cref="Timelines"/>.
    /// </summary>
    public float? SkyLightLevel { get; set; }

    /// <summary>
    /// Whether the type's time is fixed (its <c>has_fixed_time</c>), so it's never dark outside for beds; null for
    /// vanilla's types, which the server knows. Not written, like <see cref="Timelines"/>.
    /// </summary>
    public bool? HasFixedTime { get; set; }

    /// <summary>
    /// The boolean <c>minecraft:gameplay/...</c> attributes the server applies, by identifier, as the type's own map
    /// sets them (its values applied to the attributes' defaults); null for vanilla's types, which the server knows.
    /// Not written, like <see cref="Timelines"/>.
    /// </summary>
    public IReadOnlyDictionary<string, bool>? GameplayAttributes { get; set; }

    /// <summary>
    /// Writes the fields this element models as vanilla's DimensionType.NETWORK_CODEC does. Clients get vanilla's
    /// dimension types by name, and plugins' with the data their files decode to, attributes included.
    /// </summary>
    public void Write(INbtWriter writer)
    {
        writer.WriteBool("has_skylight", this.HasSkylight);
        writer.WriteBool("has_ceiling", this.HasCeiling);
        writer.WriteDouble("coordinate_scale", this.CoordinateScale);
        writer.WriteInt("min_y", this.MinY);
        writer.WriteInt("height", this.Height);
        writer.WriteInt("logical_height", this.LogicalHeight);
        writer.WriteString("infiniburn", this.Infiniburn);
        writer.WriteFloat("ambient_light", this.AmbientLight);

        // A constant provider is written as a plain int.
        if (this.MonsterSpawnLightLevel.IntValue is int level)
        {
            writer.WriteInt("monster_spawn_light_level", level);
        }
        else if (this.MonsterSpawnLightLevel.Value is MonsterSpawnLightLevelValue provider)
        {
            writer.WriteTag(new NbtCompound("monster_spawn_light_level")
            {
                new NbtTag<string>("type", provider.Type ?? "minecraft:uniform"),
                new NbtTag<int>("min_inclusive", provider.MinInclusive),
                new NbtTag<int>("max_inclusive", provider.MaxInclusive)
            });
        }

        writer.WriteInt("monster_spawn_block_light_limit", this.MonsterSpawnBlockLightLimit);
    }
}

/// <summary>
/// A dimension type's monster spawn light level: a constant, or a provider with a range.
/// </summary>
/// <remarks>
/// A plugin's provider other than a constant, uniform or biased-to-bottom one is kept here by its type and bounds;
/// clients are sent it exactly as its file has it, and the server samples
/// <see cref="DimensionElement.MonsterSpawnLightProvider"/>.
/// </remarks>
public sealed record class MonsterSpawnLightLevel
{
    public MonsterSpawnLightLevelValue? Value { get; set; }

    public int? IntValue { get; set; }
}

public record struct MonsterSpawnLightLevelValue
{
    public int MaxInclusive { get; set; }

    public int MinInclusive { get; set; }

    public string? Type { get; set; }
}
