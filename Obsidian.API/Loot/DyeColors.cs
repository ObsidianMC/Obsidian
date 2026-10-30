namespace Obsidian.API.Loot;

/// <summary>
/// The dye colors' texture diffuse colors and vanilla's mixing of dyes into a dyed item's color.
/// </summary>
internal static class DyeColors
{
    /// <summary>
    /// The diffuse colors (RGB) in vanilla's <c>DyeColor</c> order: white, orange, magenta, light blue, yellow, lime,
    /// pink, gray, light gray, cyan, purple, blue, brown, green, red and black.
    /// </summary>
    public static readonly int[] Diffuse =
    [
        0xF9FFFE, 0xF9801D, 0xC74EBD, 0x3AB3DA, 0xFED83D, 0x80C71F, 0xF38BAA, 0x474F52,
        0x9D9D97, 0x169C9C, 0x8932B8, 0x3C44AA, 0x835432, 0x5E7C16, 0xB02E26, 0x1D1D21
    ];

    /// <summary>
    /// Mixes dye colors into an item's current color (null when undyed), like vanilla's <c>DyedItemColor.applyDyes</c>:
    /// the average color, brightened to the average of each color's brightest channel.
    /// </summary>
    public static int Mix(int? current, IReadOnlyList<int> dyes)
    {
        var red = 0;
        var green = 0;
        var blue = 0;
        var brightness = 0;
        var count = 0;

        IEnumerable<int> colors = current is int color ? [color, .. dyes] : dyes;
        foreach (var rgb in colors)
        {
            var (r, g, b) = Channels(rgb);
            brightness += Math.Max(r, Math.Max(g, b));
            red += r;
            green += g;
            blue += b;
            count++;
        }

        var averageRed = red / count;
        var averageGreen = green / count;
        var averageBlue = blue / count;
        var averageBrightness = (float)brightness / count;
        var brightest = (float)Math.Max(averageRed, Math.Max(averageGreen, averageBlue));

        var mixedRed = (int)(averageRed * averageBrightness / brightest);
        var mixedGreen = (int)(averageGreen * averageBrightness / brightest);
        var mixedBlue = (int)(averageBlue * averageBrightness / brightest);
        return mixedRed << 16 | mixedGreen << 8 | mixedBlue;
    }

    private static (int Red, int Green, int Blue) Channels(int rgb) =>
        ((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
}
