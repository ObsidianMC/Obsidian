namespace Obsidian.API.Commands.ArgumentParsers;

[ArgumentParser("minecraft:vec3")]
public sealed partial class LocationArgumentParser : BaseArgumentParser<VectorF>
{
    public override bool TryParseArgument(string input, CommandContext context, out VectorF result)
    {
        var splitted = input.Split(' ');
        var location = new VectorF();
        var player = context.Player;

        for (var i = 0; i < splitted.Length; i++)
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
            else if (text.StartsWith('~'))
            {
                if (player is null)
                {
                    result = default;
                    return false;
                }

                switch (i)
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
        }

        result = location;
        return true;
    }
}
