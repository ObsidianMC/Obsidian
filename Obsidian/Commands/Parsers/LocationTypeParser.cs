using Obsidian.Util.DataTypes;
using Qmmands;
using System;
using System.Threading.Tasks;

namespace Obsidian.Commands.Parsers
{
    public class LocationTypeParser : TypeParser<Position>
    {
        public override ValueTask<TypeParserResult<Position>> ParseAsync(Parameter parameter, string value, CommandContext context)
        {
            var splitted = value.Split(' ');
            var location = new Position();

            int count = 0;
            var ctx = (ObsidianContext)context;
            foreach (var text in splitted)
            {
                if (double.TryParse(text, out var doubleResult))
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
                    switch (count)
                    {
                        case 0:
                            location.X = ctx.Player.Location.X;
                            break;
                        case 1:
                            location.Y = ctx.Player.Location.Y;
                            break;
                        case 2:
                            location.Z = ctx.Player.Location.Z;
                            break;
                        default:
                            throw new IndexOutOfRangeException("Count went out of range");
                    }
                }
                count++;
            }

            return new ValueTask<TypeParserResult<Position>>(TypeParserResult<Position>.Successful(location));
        }
    }
}
