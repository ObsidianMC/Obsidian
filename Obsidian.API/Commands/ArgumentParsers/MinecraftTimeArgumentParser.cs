namespace Obsidian.API.Commands.ArgumentParsers;

/// <summary>
/// Reads a duration like vanilla's <c>time</c> arguments: a whole number followed by <c>d</c> for days, <c>s</c> for
/// seconds or <c>t</c> for ticks. A number without a unit is read as days.
/// </summary>
[ArgumentParser("minecraft:time")]
public sealed partial class MinecraftTimeArgumentParser : BaseArgumentParser<MinecraftTime>
{
    /// <summary>
    /// Gets or sets the shortest duration accepted, in ticks. The client is told it too.
    /// </summary>
    public int Min { get; set; }

    /// <inheritdoc />
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

    /// <summary>
    /// Writes the parser's id and <see cref="Min"/>.
    /// </summary>
    /// <param name="writer">The stream to write to.</param>
    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteInt(Min);
    }
}
