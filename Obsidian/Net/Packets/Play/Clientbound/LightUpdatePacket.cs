namespace Obsidian.Net.Packets.Play.Clientbound;

/// <summary>
/// Updates the light of a chunk the client already has, like vanilla's <c>ClientboundLightUpdatePacket</c>.
/// </summary>
public partial class LightUpdatePacket
{
    public IChunk Chunk { get; }

    private readonly BitSet skyMask = new();
    private readonly BitSet blockMask = new();
    private readonly BitSet emptySkyMask = new();
    private readonly BitSet emptyBlockMask = new();
    private readonly List<byte[]> sky = [];
    private readonly List<byte[]> block = [];

    /// <summary>Snapshots all build-range sections. Call while holding the level's block-write lock.</summary>
    public LightUpdatePacket(IChunk chunk)
        : this(
            chunk,
            Enumerable.Range(0, chunk.Sections.Length).ToHashSet(),
            Enumerable.Range(0, chunk.Sections.Length).ToHashSet())
    {
    }

    /// <summary>Snapshots only changed sections; zero arrays use vanilla's empty section masks.</summary>
    internal LightUpdatePacket(IChunk chunk, IReadOnlySet<int> skySections, IReadOnlySet<int> blockSections)
    {
        this.Chunk = chunk;
        for (var index = 0; index < chunk.Sections.Length; index++)
        {
            var section = chunk.Sections[index];
            if (skySections.Contains(index))
                Capture(index, section.SkyLightArray, this.skyMask, this.emptySkyMask, this.sky);

            if (blockSections.Contains(index))
                Capture(index, section.BlockLightArray, this.blockMask, this.emptyBlockMask, this.block);
        }
    }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(Chunk.X);
        writer.WriteVarInt(Chunk.Z);

        WriteMask(writer, this.skyMask);
        WriteMask(writer, this.blockMask);
        WriteMask(writer, this.emptySkyMask);
        WriteMask(writer, this.emptyBlockMask);
        WriteArrays(writer, this.sky);
        WriteArrays(writer, this.block);
    }

    private static void Capture(
        int index, ReadOnlyMemory<byte> data, BitSet mask, BitSet emptyMask, List<byte[]> arrays)
    {
        // Protocol bit zero is the section below the build range.
        if (data.Span.IndexOfAnyExcept((byte)0) < 0)
        {
            emptyMask.SetBit(index + 1, true);
        }
        else
        {
            mask.SetBit(index + 1, true);
            arrays.Add(data.ToArray());
        }
    }

    private static void WriteMask(INetStreamWriter writer, BitSet mask)
    {
        writer.WriteVarInt(mask.DataStorage.Length);
        if (mask.DataStorage.Length != 0)
            writer.WriteLongArray(mask.DataStorage.ToArray());
    }

    private static void WriteArrays(INetStreamWriter writer, List<byte[]> arrays)
    {
        writer.WriteVarInt(arrays.Count);
        foreach (var array in arrays)
        {
            writer.WriteVarInt(array.Length);
            writer.WriteByteArray(array);
        }
    }
}
