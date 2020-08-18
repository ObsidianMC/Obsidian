using Obsidian.Util.DataTypes;
using Qmmands;
using System;
using System.Threading.Tasks;

namespace Obsidian.Commands.Parsers
{
    public class LocationTypeParser : TypeParser<Position>
    {
        public override Task<TypeParserResult<Position>> ParseAsync(Parameter parameter, string value, ICommandContext context, IServiceProvider provider)
        {
            var splitted = value.Split(' ');
            var location = new Position();

            int count = 0;
            var ctx = (CommandContext)context;
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
                            location.X = ctx.Player.Transform.X;
                            break;
                        case 1:
                            location.Y = ctx.Player.Transform.Y;
                            break;
                        case 2:
                            location.Z = ctx.Player.Transform.Z;
                            break;
                        default:
                            throw new IndexOutOfRangeException("Count went out of range");
                    }
                }
                count++;
            }

            return Task.FromResult(TypeParserResult<Position>.Successful(location));
        }
    }
}
