using Obsidian.API.Registry.Codecs.Biomes;

namespace Obsidian.WorldData.Generators.Biomes;

/// <summary>
/// The biomes the BIOMES status stores in one chunk, by quart (see <see cref="ClimateSource.Stored"/>). Read only, so
/// threads share it.
/// </summary>
internal sealed class StoredBiomes
{
    private readonly IReadOnlyList<BiomeCodec> palette;
    private readonly byte[] indices;

    /// <param name="palette">The biomes <paramref name="indices"/> number.</param>
    /// <param name="indices">Each quart's biome in <paramref name="palette"/>, at <see cref="Index"/>.</param>
    public StoredBiomes(IReadOnlyList<BiomeCodec> palette, byte[] indices, int minY, int height)
    {
        this.palette = palette;
        this.indices = indices;
        this.MinY = minY;
        this.Height = height;
    }

    /// <summary>The chunk's build range, in blocks.</summary>
    public int MinY { get; }

    public int Height { get; }

    public int QuartHeight => this.Height >> 2;

    /// <summary>How many quarts a chunk of <paramref name="height"/> blocks stores.</summary>
    public static int Size(int height) => 16 * (height >> 2);

    /// <param name="x">The quart's x within the chunk, 0 to 3.</param>
    /// <param name="quartY">The quart's y above the chunk's lowest quart.</param>
    /// <param name="z">The quart's z within the chunk, 0 to 3.</param>
    public static int Index(int x, int quartY, int z) => (quartY << 4) | (x << 2) | z;

    /// <inheritdoc cref="Index"/>
    public BiomeCodec Get(int x, int quartY, int z) => this.palette[this.indices[Index(x, quartY, z)]];
}
