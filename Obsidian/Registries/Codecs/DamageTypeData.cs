using Obsidian.API.Registry.Codecs.DamageTypes;
using Obsidian.Nbt;
using System.Text.Json;
using static Obsidian.Registries.Codecs.CodecJson;

namespace Obsidian.Registries.Codecs;

/// <summary>
/// Reads and writes a plugin's damage type like vanilla 1.21.11's DamageType.DIRECT_CODEC, which is both the data
/// pack codec and the one clients decode with.
/// </summary>
internal static class DamageTypeData
{
    private static readonly string[] scalings = ["never", "when_caused_by_living_non_player", "always"];

    private static readonly string[] effectNames = ["hurt", "thorns", "drowning", "burning", "poking", "freezing"];

    private static readonly string[] deathMessageTypes = ["default", "fall_variants", "intentional_game_design"];

    public static DamageTypeElement Read(JsonElement root)
    {
        Object(root, "");

        var scaling = Enum(Required(root, "", "scaling"), "scaling", scalings);

        var effects = TryGet(root, "effects", out var effectsValue)
            ? Enum(effectsValue, "effects", effectNames)
            : "hurt";

        var deathMessageType = TryGet(root, "death_message_type", out var deathMessageValue)
            ? Enum(deathMessageValue, "death_message_type", deathMessageTypes)
            : "default";

        return new DamageTypeElement
        {
            MessageId = String(Required(root, "", "message_id"), "message_id"),
            Scaling = ParseName<DamageScaling>(scaling),
            Exhaustion = Float(Required(root, "", "exhaustion"), "exhaustion"),
            Effects = ParseName<DamageEffects>(effects),
            DeathMessageType = ParseName<DeathMessageType>(deathMessageType)
        };
    }

    /// <summary>
    /// The damage type as vanilla writes it: the default effects (hurt) and death message type (default) are left
    /// out.
    /// </summary>
    public static NbtCompound ToNetworkNbt(DamageTypeElement element)
    {
        var compound = new NbtCompound
        {
            new NbtTag<string>("message_id", element.MessageId),
            new NbtTag<string>("scaling", element.Scaling.ToString().ToSnakeCase()),
            new NbtTag<float>("exhaustion", element.Exhaustion)
        };

        if (element.Effects is DamageEffects effects && effects != DamageEffects.Hurt)
            compound.Add(new NbtTag<string>("effects", effects.ToString().ToSnakeCase()));

        if (element.DeathMessageType is DeathMessageType type && type != DeathMessageType.Default)
            compound.Add(new NbtTag<string>("death_message_type", type.ToString().ToSnakeCase()));

        return compound;
    }

    // A serialized name, like when_caused_by_living_non_player, as its enum value.
    private static TEnum ParseName<TEnum>(string name) where TEnum : struct, System.Enum =>
        System.Enum.Parse<TEnum>(name.Replace("_", string.Empty), ignoreCase: true);
}
