namespace Obsidian.API.Commands.ArgumentParsers;

public sealed class MinecraftTimeArgumentParser : BaseArgumentParser<MinecraftTime>
{
    public MinecraftTimeArgumentParser() : base(42, "minecraft:time")
    {
    }

    public override bool TryParseArgument(string input, CommandContext ctx, out MinecraftTime result)
    {
        var lastChar = input.LastOrDefault();
        var isSuccess = false;

        result = default;

        if (lastChar == 'd' && int.TryParse(input.TrimEnd('d'), out var dayTime))
        {
            result = MinecraftTime.FromDay(dayTime);

            isSuccess = true;
        }
        else if (lastChar == 't' && int.TryParse(input.TrimEnd('t'), out var tickTime))
        {
            result = MinecraftTime.FromTick(tickTime);

            isSuccess = true;
        }
        else if (lastChar == 's' && int.TryParse(input.TrimEnd('s'), out var secondsTime))
        {
            result = MinecraftTime.FromSecond(secondsTime);

            isSuccess = true;
        }
        else if (int.TryParse(input, out var intValue))
        {
            result = MinecraftTime.FromDay(intValue);

            isSuccess = true;
        }

        return isSuccess;
    }
}
