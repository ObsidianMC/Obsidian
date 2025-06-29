namespace Obsidian.API.Commands.ArgumentParsers;

[ArgumentParser("minecraft:time")]
public sealed partial class MinecraftTimeArgumentParser : BaseArgumentParser<MinecraftTime>
{
    public int Min { get; set; } = 0;

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

        if (result.Tick < Min)
        {
            result = default;
            return false;
        }

        return isSuccess;
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteInt(Min);
    }
}
