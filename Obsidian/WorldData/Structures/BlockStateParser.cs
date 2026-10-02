namespace Obsidian.WorldData.Structures;

/// <summary>
/// Parses block states written like <c>minecraft:oak_stairs[facing=east,half=bottom]</c>, as vanilla's
/// <c>BlockStateParser.parseForBlock</c> reads jigsaw <c>final_state</c> values.
/// </summary>
internal static class BlockStateParser
{
    /// <summary>
    /// The block state, or <c>null</c> when the block, a property or a value is unknown. Unlisted properties keep their
    /// defaults, and anything after the properties (block entity data, stray characters) is ignored like vanilla does.
    /// </summary>
    public static IBlock? TryParse(string text)
    {
        var bracket = text.IndexOf('[');
        var nameEnd = bracket >= 0 ? bracket : text.IndexOf('{');
        var name = (nameEnd >= 0 ? text[..nameEnd] : text).Trim();
        if (!name.Contains(':'))
            name = "minecraft:" + name;

        var properties = new Dictionary<string, string>();
        if (bracket >= 0)
        {
            var close = text.IndexOf(']', bracket);
            if (close < 0)
                return null;

            foreach (var entry in text[(bracket + 1)..close].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var separator = entry.IndexOf('=');
                if (separator < 0)
                    return null;

                properties[entry[..separator].Trim()] = entry[(separator + 1)..].Trim();
            }
        }

        IBlock state;
        try
        {
            state = BlockStateProperties.GetState(name, properties);
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        foreach (var (property, value) in properties)
        {
            if (state.GetProperty(property) != value)
                return null;
        }

        return state;
    }
}
