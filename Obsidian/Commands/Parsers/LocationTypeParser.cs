using Obsidian.API;
using Obsidian.CommandFramework;
using Obsidian.CommandFramework.ArgumentParsers;
using Obsidian.Entities;
using System;

namespace Obsidian.Commands.Parsers
{
    public class LocationTypeParser : BaseArgumentParser<PositionF>
    {
        public LocationTypeParser() : base("minecraft:vec3") { }
        public override bool TryParseArgument(string input, ObsidianContext context, out PositionF result)
        {
            result = default;

            var splitted = input.Split(' ');
            var location = new PositionF();

            int count = 0;
            var ctx = context;
            foreach (var text in splitted)
            {
                if (float.TryParse(text, out var doubleResult))
                {
                    switch (count)
                    {
                        case 0:
                            location.X = doubleResult;
                            break;
                        case 1:
                            location.Y = doubleResult;
                            break;
                        case 2:
                            location.Z = doubleResult;
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
