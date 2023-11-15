namespace Obsidian.SourceGenerators.Registry.Models;

internal static class Customizations
{
    public static HashSet<string> BannedNames { get; }
    public static Dictionary<string, string> RenamedEnums { get; }

    static Customizations()
    {
        BannedNames = new();
        foreach (Type type in typeof(int).Assembly.GetTypes())
        {
            BannedNames.Add(type.Name);
        }
        BannedNames.Add("Half");

        RenamedEnums = new()
        {
            { GetEnumTag("x", "z"), "HorizontalAxis" }, // vs. Axis(x,y,z)
            { GetEnumTag("north", "east", "south", "west", "up", "down"), "BlockFace" }, // vs. Facing(north, east, south, west)
            { GetEnumTag("up", "side", "none"), "WireShape" },
            { GetEnumTag("upper", "lower"), "BlockHalf" },
            { GetEnumTag("top", "bottom"), "StairHalf" },
            { GetEnumTag("down", "north", "south", "west", "east"), "HopperFace" },
            { GetEnumTag("north_south", "east_west", "ascending_east", "ascending_west", "ascending_north", "ascending_south", "south_east", "south_west", "north_west", "north_east"), "RailShape" },
            { GetEnumTag("north_south", "east_west", "ascending_east", "ascending_west", "ascending_north", "ascending_south"), "SpecialRailShape" },
            { GetEnumTag("straight", "inner_left", "inner_right", "outer_left", "outer_right"), "StairsShape" },
            { GetEnumTag("top", "bottom", "double"), "SlabType" },
            { GetEnumTag("normal", "sticky"), "PistonType" },
            { GetEnumTag("single", "left", "right"), "ChestType" },
            { GetEnumTag("none", "low", "tall"), "WallConnection" }, // for East, West, North, South
            { GetEnumTag("save", "load", "corner", "data"), "StructureMode" },
            { GetEnumTag("compare", "subtract"), "ComparatorMode" },
            { GetEnumTag("none", "small", "large"), "LeavesType" },
        };
    }

    internal static string GetEnumTag(params string[] values)
    {
        int hash = 0;
        foreach (string value in values)
        {
            hash ^= value.GetHashCode();
        }
        return $"enum{hash}";
    }
}
