using Obsidian.API.Registry.Codecs.Dimensions;
using Obsidian.Nbt;
using Obsidian.Providers.IntProviders;
using System.Text.Json;
using static Obsidian.Registries.Codecs.CodecJson;

namespace Obsidian.Registries.Codecs;

/// <summary>
/// A plugin's dimension type as vanilla 1.21.11's DimensionType.DIRECT_CODEC reads a data pack's, with its ranges
/// and the checks its constructor makes.
/// </summary>
/// <param name="TimelineTag">The tag of the timelines, or null for none.</param>
internal sealed record DimensionTypeData(
    bool HasFixedTime,
    bool HasSkylight,
    bool HasCeiling,
    double CoordinateScale,
    int MinY,
    int Height,
    int LogicalHeight,
    string Infiniburn,
    float AmbientLight,
    IIntProvider MonsterSpawnLightLevel,
    int MonsterSpawnBlockLightLimit,
    string Skybox,
    string CardinalLight,
    EnvironmentAttributeMap Attributes,
    string? TimelineTag)
{
    // Vanilla's bounds for Y, from the 12 bits a block position packs it in (DimensionType.MIN_Y, MAX_Y, Y_SIZE).
    private const int LowestY = -2032;
    private const int HighestY = 2031;
    private const int MaxHeight = 4064;

    public static DimensionTypeData Read(JsonElement root)
    {
        Object(root, "");

        var minY = Int(Required(root, "", "min_y"), "min_y", LowestY, HighestY);
        var height = Int(Required(root, "", "height"), "height", 16, MaxHeight);
        var logicalHeight = Int(Required(root, "", "logical_height"), "logical_height", 0, MaxHeight);

        var data = new DimensionTypeData(
            HasFixedTime: TryGet(root, "has_fixed_time", out var fixedTime) && Bool(fixedTime, "has_fixed_time"),
            HasSkylight: Bool(Required(root, "", "has_skylight"), "has_skylight"),
            HasCeiling: Bool(Required(root, "", "has_ceiling"), "has_ceiling"),
            CoordinateScale: Double(Required(root, "", "coordinate_scale"), "coordinate_scale", 1.0E-5F, 3.0E7),
            MinY: minY,
            Height: height,
            LogicalHeight: logicalHeight,
            Infiniburn: Tag(Required(root, "", "infiniburn"), "infiniburn"),
            AmbientLight: Float(Required(root, "", "ambient_light"), "ambient_light"),
            MonsterSpawnLightLevel: IntProviderCodec.Read(
                Required(root, "", "monster_spawn_light_level"), "monster_spawn_light_level", 0, 15),
            MonsterSpawnBlockLightLimit: Int(
                Required(root, "", "monster_spawn_block_light_limit"), "monster_spawn_block_light_limit", 0, 15),
            Skybox: TryGet(root, "skybox", out var skybox)
                ? Enum(skybox, "skybox", "none", "overworld", "end")
                : "overworld",
            CardinalLight: TryGet(root, "cardinal_light", out var cardinalLight)
                ? Enum(cardinalLight, "cardinal_light", "default", "nether")
                : "default",
            Attributes: TryGet(root, "attributes", out var attributes)
                ? EnvironmentAttributes.Read(attributes, "attributes", onlyPositional: false)
                : EnvironmentAttributeMap.Empty,
            TimelineTag: TryGet(root, "timelines", out var timelines) ? ReadTimelines(timelines) : null);

        // The checks of vanilla's DimensionType constructor.
        if (minY + height > HighestY + 1)
            throw Error("height", $"min_y + height ({minY + height}) can't be more than {HighestY + 1}");

        if (logicalHeight > height)
            throw Error("logical_height", $"{logicalHeight} can't be more than height ({height})");

        if (height % 16 != 0)
            throw Error("height", $"{height} isn't a multiple of 16");

        if (minY % 16 != 0)
            throw Error("min_y", $"{minY} isn't a multiple of 16");

        return data;
    }

    public DimensionElement ToElement() => new()
    {
        HasSkylight = this.HasSkylight,
        HasCeiling = this.HasCeiling,
        CoordinateScale = this.CoordinateScale,
        MinY = this.MinY,
        Height = this.Height,
        LogicalHeight = this.LogicalHeight,
        Infiniburn = $"#{this.Infiniburn}",
        AmbientLight = this.AmbientLight,
        MonsterSpawnLightLevel = this.MonsterSpawnLightLevel is ConstantIntProvider constant
            ? new MonsterSpawnLightLevel { IntValue = constant.Value }
            : new MonsterSpawnLightLevel
            {
                Value = new MonsterSpawnLightLevelValue
                {
                    Type = this.MonsterSpawnLightLevel.Type,
                    MinInclusive = this.MonsterSpawnLightLevel.MinValue,
                    MaxInclusive = this.MonsterSpawnLightLevel.MaxValue
                }
            },
        MonsterSpawnLightProvider = this.MonsterSpawnLightLevel,
        MonsterSpawnBlockLightLimit = this.MonsterSpawnBlockLightLimit,
        Timelines = this.TimelineTag is null ? null : $"#{this.TimelineTag}",

        // The attributes' defaults are false and 15; the dimension type's map applies first, as in vanilla.
        MonstersBurn = this.Attributes.Apply("minecraft:gameplay/monsters_burn", false),
        SkyLightLevel = this.Attributes.Apply("minecraft:gameplay/sky_light_level", 15f)
    };

    /// <summary>
    /// The dimension type as vanilla's DimensionType.NETWORK_CODEC writes it, which is what clients decode: every
    /// field, with the synced attributes, and the defaults vanilla leaves out left out.
    /// </summary>
    public NbtCompound ToNetworkNbt()
    {
        var compound = new NbtCompound();

        if (this.HasFixedTime)
            compound.Add(new NbtTag<byte>("has_fixed_time", 1));

        compound.Add(new NbtTag<byte>("has_skylight", this.HasSkylight ? (byte)1 : (byte)0));
        compound.Add(new NbtTag<byte>("has_ceiling", this.HasCeiling ? (byte)1 : (byte)0));
        compound.Add(new NbtTag<double>("coordinate_scale", this.CoordinateScale));
        compound.Add(new NbtTag<int>("min_y", this.MinY));
        compound.Add(new NbtTag<int>("height", this.Height));
        compound.Add(new NbtTag<int>("logical_height", this.LogicalHeight));
        compound.Add(new NbtTag<string>("infiniburn", $"#{this.Infiniburn}"));
        compound.Add(new NbtTag<float>("ambient_light", this.AmbientLight));
        compound.Add(IntProviderCodec.ToNbt(this.MonsterSpawnLightLevel, "monster_spawn_light_level"));
        compound.Add(new NbtTag<int>("monster_spawn_block_light_limit", this.MonsterSpawnBlockLightLimit));

        if (this.Skybox != "overworld")
            compound.Add(new NbtTag<string>("skybox", this.Skybox));

        if (this.CardinalLight != "default")
            compound.Add(new NbtTag<string>("cardinal_light", this.CardinalLight));

        if (this.Attributes.Entries.Length > 0)
            compound.Add(this.Attributes.ToNetworkNbt("attributes"));

        if (this.TimelineTag is not null)
            compound.Add(new NbtTag<string>("timelines", $"#{this.TimelineTag}"));

        return compound;
    }

    // Vanilla takes a holder set of timelines: a tag, an identifier or a list of them. The server doesn't sync the
    // timeline registry, only its tags, so clients could only decode a tag (or no timelines).
    private static string? ReadTimelines(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 0)
            return null;

        if (value.ValueKind != JsonValueKind.String || !value.GetString()!.StartsWith('#'))
            throw Error("timelines", "plugins can only give a tag of timelines, like \"#minecraft:in_overworld\"");

        var tag = Tag(value, "timelines");

        return TagsRegistry.TryGet("timeline", tag, out _)
            ? tag
            : throw Error("timelines", $"#{tag} isn't a timeline tag");
    }
}
