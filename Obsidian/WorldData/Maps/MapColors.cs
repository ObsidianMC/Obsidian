using System.IO;
using System.Reflection;

namespace Obsidian.WorldData.Maps;

/// <summary>
/// Vanilla's map colors (<c>MapColor</c>): the color id every block state shows on maps, and how ids and brightness pack into
/// the bytes of a map.
/// </summary>
/// <remarks>
/// The ids per state are loaded from <c>Assets/map_colors.bin</c>, one byte per block state id, dumped from vanilla 1.21.11
/// (<c>BlockState.getMapColor</c>).
/// </remarks>
internal static class MapColors
{
    public const byte None = 0;
    public const byte Water = 12;
    public const byte Orange = 15;
    public const byte Brown = 26;

    private static readonly Lazy<byte[]> colors = new(Load);

    /// <summary>
    /// The map color id of a block state.
    /// </summary>
    public static byte Get(IBlock block)
    {
        var id = block.GetHashCode();
        var table = colors.Value;
        return (uint)id < (uint)table.Length ? table[id] : None;
    }

    /// <summary>
    /// Vanilla <c>MapColor.getPackedId</c>: the byte a map stores for a color id shaded with <paramref name="brightness"/>.
    /// </summary>
    public static byte Pack(byte color, MapBrightness brightness) => (byte)(color << 2 | (int)brightness);

    private static byte[] Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Obsidian.Assets.map_colors.bin")
            ?? throw new InvalidOperationException("Missing map colors asset.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}

/// <summary>
/// Vanilla's <c>MapColor.Brightness</c>, by id.
/// </summary>
internal enum MapBrightness
{
    Low,
    Normal,
    High,
    Lowest
}
