using System.Globalization;
using Obsidian.API;
using Obsidian.API.World.Generator;

namespace Obsidian.Builtins.WorldEdit;

internal static class BlockTransforms
{
    private static readonly string[] horizontal = ["north", "east", "south", "west"];

    internal static Vector RotatePosition(Vector position, int degrees)
    {
        var turns = Turns(degrees);
        for (var i = 0; i < turns; i++)
            position = new Vector(checked(-position.Z), position.Y, position.X);
        return position;
    }

    internal static Vector FlipPosition(Vector position, string axis) => axis switch
    {
        "x" => new(checked(-position.X), position.Y, position.Z),
        "y" => new(position.X, checked(-position.Y), position.Z),
        "z" => new(position.X, position.Y, checked(-position.Z)),
        _ => throw new ArgumentException("Flip axis must be x, y or z.")
    };

    internal static int Turns(int degrees)
    {
        if (degrees % 90 != 0)
            throw new ArgumentException("Rotation must be a multiple of 90 degrees.");
        return (degrees % 360 + 360) % 360 / 90;
    }

    internal static IBlock RotateBlock(IBlock block, int degrees)
    {
        var turns = Turns(degrees);
        return TransformBlock(block, direction =>
        {
            var index = Array.IndexOf(horizontal, direction);
            return index < 0 ? direction : horizontal[(index + turns) % 4];
        }, turns % 2 != 0, mirrorAxis: null, turns);
    }

    internal static IBlock FlipBlock(IBlock block, string axis)
    {
        FlipPosition(default, axis);
        return TransformBlock(block, direction => (axis, direction) switch
        {
            ("x", "east") => "west", ("x", "west") => "east",
            ("z", "north") => "south", ("z", "south") => "north",
            ("y", "up") => "down", ("y", "down") => "up",
            _ => direction
        }, swapAxes: false, axis, turns: 0);
    }

    private static IBlock TransformBlock(IBlock block, Func<string, string> direction,
        bool swapAxes, string? mirrorAxis, int turns)
    {
        var state = block.ToSimpleState();
        var original = state.Properties.ToArray();
        state.Properties.Clear();
        foreach (var (key, originalValue) in original)
        {
            var value = originalValue;
            var property = direction(key);
            if (!original.Any(pair => pair.Key == property))
                property = key;
            if (key == "facing") value = direction(value);
            if (key == "axis" && swapAxes)
                value = value switch { "x" => "z", "z" => "x", _ => value };
            if (key == "rotation" && int.TryParse(value, out var rotation))
            {
                rotation = mirrorAxis switch
                {
                    "x" => (16 - rotation) % 16,
                    "z" => (24 - rotation) % 16,
                    _ => (rotation + turns * 4) % 16
                };
                value = rotation.ToString(CultureInfo.InvariantCulture);
            }
            if (key == "shape")
            {
                var parts = value.Split('_').Select(direction).ToArray();
                value = string.Join('_', parts);
                value = value switch
                {
                    "south_north" => "north_south", "west_east" => "east_west",
                    "east_north" => "north_east", "west_north" => "north_west",
                    "east_south" => "south_east", "west_south" => "south_west",
                    _ => value
                };
                if (mirrorAxis is "x" or "z")
                    value = value switch
                    {
                        "inner_left" => "inner_right", "inner_right" => "inner_left",
                        "outer_left" => "outer_right", "outer_right" => "outer_left",
                        _ => value
                    };
            }
            if (key == "hinge" && mirrorAxis is "x" or "z")
                value = value == "left" ? "right" : "left";
            if (key == "type" && mirrorAxis is "x" or "z")
                value = value switch { "left" => "right", "right" => "left", _ => value };
            if (mirrorAxis == "y" && key is "face" or "attachment")
                value = value switch { "floor" => "ceiling", "ceiling" => "floor", _ => value };
            if (mirrorAxis == "y" && key is "half" or "type")
                value = value switch { "top" => "bottom", "bottom" => "top", "upper" => "lower", "lower" => "upper", _ => value };
            state.Properties[property] = value;
        }
        return state.ToBlock();
    }
}
