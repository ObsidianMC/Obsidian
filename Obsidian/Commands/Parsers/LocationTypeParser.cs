using Obsidian.API;
using Obsidian.Entities;
using System;

namespace Obsidian.Commands.Parsers
{
    public class LocationTypeParser : BaseArgumentParser<VectorF>
    {
        public LocationTypeParser() : base("minecraft:vec3")
        {
        }

        public override bool TryParseArgument(string input, CommandContext context, out VectorF result)
        {
            var splitted = input.Split(' ');
            var location = new VectorF();

            int count = 0;
            var ctx = context;
            foreach (var text in splitted)
            {
                if (float.TryParse(text, out var floatResult))
                {
                    switch (count)
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

                }
                else if (text.Equals("~"))
                {
                    var player = (Player)ctx.Player;
                    switch (count)
                    {
                        case 0:
                            location.X = player.Position.X;
                            break;
                        case 1:
                            location.Y = player.Position.Y;
                            break;
                        case 2:
                            location.Z = player.Position.Z;
                            break;
                        default:
                            throw new IndexOutOfRangeException("Count went out of range");
                    }
                }
                count++;
            }

            result = location;
            return true;
        }
    }
}
