namespace Obsidian.API.Commands.ArgumentParsers;

[ArgumentParser("minecraft:vec3")]
public sealed partial class LocationArgumentParser : BaseArgumentParser<VectorD>
{
    public override bool TryParseArgument(string input, CommandContext context, out VectorD result)
    {
        var splitted = input.Split(' ');
        var location = new VectorD();
        var player = context.Player;

        for (var i = 0; i < splitted.Length; i++)
        {
            var text = splitted[i];
            if (double.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var number))
                switch (i)
                {
                    case 0:
                        location.X = number;
                        break;
                    case 1:
                        location.Y = number;
                        break;
                    case 2:
                        location.Z = number;
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
