namespace Obsidian.Entities;

/// <summary>
/// The ranges of vanilla's attributes and whether clients are sent them.
/// </summary>
/// <remarks>
/// Matches 26.3's <c>Attributes</c> registrations (each <c>RangedAttribute</c>'s minimum and maximum and its
/// <c>setSyncable</c> flag): clients compute the same values from <c>UpdateAttributes</c>, so the server must clamp
/// as they do and send the attributes they expect. Protocol ids come from the registries report.
/// </remarks>
internal static class VanillaAttributes
{
    internal readonly record struct Range(double Min, double Max, bool Syncable);

    private static readonly Dictionary<string, Range> ranges = new()
    {
        ["minecraft:air_drag_modifier"] = new(0, 2048, true),
        ["minecraft:armor"] = new(0, 30, true),
        ["minecraft:armor_toughness"] = new(0, 20, true),
        ["minecraft:attack_damage"] = new(0, 2048, false),
        ["minecraft:attack_knockback"] = new(0, 5, false),
        ["minecraft:attack_speed"] = new(0, 1024, true),
        ["minecraft:below_name_distance"] = new(0, 512, true),
        ["minecraft:block_break_speed"] = new(0, 1024, true),
        ["minecraft:block_interaction_range"] = new(0, 64, true),
        ["minecraft:bounciness"] = new(0, 1, true),
        ["minecraft:burning_time"] = new(0, 1024, true),
        ["minecraft:camera_distance"] = new(0, 32, true),
        ["minecraft:explosion_knockback_resistance"] = new(0, 1, true),
        ["minecraft:entity_interaction_range"] = new(0, 64, true),
        ["minecraft:fall_damage_multiplier"] = new(0, 100, true),
        ["minecraft:flying_speed"] = new(0, 1024, true),
        ["minecraft:follow_range"] = new(0, 2048, false),
        ["minecraft:friction_modifier"] = new(0, 2048, true),
        ["minecraft:gravity"] = new(-1, 1, true),
        ["minecraft:jump_strength"] = new(0, 32, true),
        ["minecraft:knockback_resistance"] = new(-2, 1, false),
        ["minecraft:luck"] = new(-1024, 1024, true),
        ["minecraft:max_absorption"] = new(0, 2048, true),
        ["minecraft:max_health"] = new(1, 1024, true),
        ["minecraft:mining_efficiency"] = new(0, 1024, true),
        ["minecraft:movement_efficiency"] = new(0, 1, true),
        ["minecraft:movement_speed"] = new(0, 1024, true),
        ["minecraft:name_tag_distance"] = new(0, 512, true),
        ["minecraft:oxygen_bonus"] = new(0, 1024, true),
        ["minecraft:safe_fall_distance"] = new(-1024, 1024, true),
        ["minecraft:scale"] = new(0.0625, 16, true),
        ["minecraft:sneaking_speed"] = new(0, 1, true),
        ["minecraft:spawn_reinforcements"] = new(0, 1, false),
        ["minecraft:step_height"] = new(0, 10, true),
        ["minecraft:submerged_mining_speed"] = new(0, 20, true),
        ["minecraft:sweeping_damage_ratio"] = new(0, 1, true),
        ["minecraft:tempt_range"] = new(0, 2048, false),
        ["minecraft:water_movement_efficiency"] = new(0, 1, true),
        ["minecraft:waypoint_transmit_range"] = new(0, 6e7, false),
        ["minecraft:waypoint_receive_range"] = new(0, 6e7, false)
    };

    private static readonly Lazy<Dictionary<string, int>> protocolIds = new(() =>
        Obsidian.Registries.VanillaRegistryIds.Get("minecraft:attribute")
            .Select((name, id) => (name, id))
            .ToDictionary(entry => entry.name, entry => entry.id));

    /// <summary>
    /// The attribute's range, if it's one of vanilla's.
    /// </summary>
    public static bool TryGetRange(string name, out Range range) => ranges.TryGetValue(name, out range);

    /// <summary>
    /// The attribute's id in the <c>minecraft:attribute</c> registry, which <c>UpdateAttributes</c> names it by.
    /// </summary>
    public static bool TryGetProtocolId(string name, out int id) => protocolIds.Value.TryGetValue(name, out id);

    /// <summary>
    /// The attribute's name for its id in the <c>minecraft:attribute</c> registry, like item modifiers carry.
    /// </summary>
    public static string? NameOf(int protocolId)
    {
        var names = Obsidian.Registries.VanillaRegistryIds.Get("minecraft:attribute");

        return protocolId >= 0 && protocolId < names.Count ? names[protocolId] : null;
    }

    /// <summary>
    /// Clamps a computed value into the attribute's range, like vanilla's <c>RangedAttribute.sanitizeValue</c>: NaN
    /// becomes the minimum. Attributes vanilla doesn't have are returned as they are.
    /// </summary>
    public static double Sanitize(string name, double value)
    {
        if (!ranges.TryGetValue(name, out var range))
            return value;

        return double.IsNaN(value) ? range.Min : Math.Clamp(value, range.Min, range.Max);
    }
}
