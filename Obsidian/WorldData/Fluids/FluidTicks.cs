using System.Diagnostics.CodeAnalysis;

namespace Obsidian.WorldData.Fluids;

/// <summary>
/// Vanilla's fluid registry names, which saved ticks use.
/// </summary>
internal static class FluidNames
{
    public static string Name(this FluidKind fluid) => fluid switch
    {
        FluidKind.Water => "minecraft:water",
        FluidKind.FlowingWater => "minecraft:flowing_water",
        FluidKind.Lava => "minecraft:lava",
        FluidKind.FlowingLava => "minecraft:flowing_lava",
        _ => "minecraft:empty"
    };

    public static FluidKind Parse(string? name) => name switch
    {
        "minecraft:water" => FluidKind.Water,
        "minecraft:flowing_water" => FluidKind.FlowingWater,
        "minecraft:lava" => FluidKind.Lava,
        "minecraft:flowing_lava" => FluidKind.FlowingLava,
        _ => FluidKind.Empty
    };

    /// <summary>
    /// Reads a saved fluid tick's fluid; ticks of the empty fluid are dropped, like vanilla's.
    /// </summary>
    public static bool TryParse(string name, [MaybeNullWhen(false)] out FluidKind fluid)
    {
        fluid = Parse(name);
        return fluid != FluidKind.Empty;
    }
}
