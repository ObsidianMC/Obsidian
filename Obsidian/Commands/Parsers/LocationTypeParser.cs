﻿using Obsidian.Entities;

namespace Obsidian.Commands.Parsers;

public class LocationTypeParser : BaseArgumentParser<VectorF>
{
    public LocationTypeParser() : base(10, "minecraft:vec3")
    {
    }

    public override bool TryParseArgument(string input, CommandContext context, out VectorF result)
    {
        var splitted = input.Split(' ');
        var location = new VectorF();

        for (int i = 0; i < splitted.Length; i++)
        {
            var text = splitted[i];
            if (float.TryParse(text, out var floatResult))
                switch (i)
                {
                    case 0:
                        location.X = floatResult;
                        break;
                    case 1:
                        location.Y = floatResult;
                        break;
                    case 2:
                        location.Z = floatResult;
                        break;
                    default:
                        throw new IndexOutOfRangeException("Count went out of range");
                }
            else if (text.StartsWith("~"))
            {
                if (context.Player is not Player player)
                {
                    result = default;
                    return false;
                }

                float.TryParse(text.Replace("~", ""), out float relative);
                switch (i)
                {
                    case 0:
                        location.X = player.Position.X + relative;
                        break;
                    case 1:
                        location.Y = player.Position.Y + relative;
                        break;
                    case 2:
                        location.Z = player.Position.Z + relative;
                        break;
                    default:
                        throw new IndexOutOfRangeException("Count went out of range");
                }
            }
        }

        result = location;
        return true;
    }
}
