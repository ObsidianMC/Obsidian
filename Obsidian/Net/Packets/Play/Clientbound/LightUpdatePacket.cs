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
    private readonly List<ReadOnlyMemory<byte>> sky = [];
    private readonly List<ReadOnlyMemory<byte>> block = [];

    // Sections lit to 15 throughout (all the sky light above the terrain) share this snapshot rather than copying their
    // own; it's never written.
    private static readonly ReadOnlyMemory<byte> FullLight = CreateFullLight();

    /// <summary>
    /// Snapshots all build-range sections. Call under the propagation gate, or on immutable storage.
    /// </summary>
    public LightUpdatePacket(IChunk chunk)
    {
        this.Chunk = chunk;
        for (var index = 0; index < chunk.Sections.Length; index++)
            this.CaptureSection(index, sky: true, block: true);
    }

    /// <summary>Snapshots only changed sections; zero arrays use vanilla's empty section masks.</summary>
    internal LightUpdatePacket(IChunk chunk, IReadOnlySet<int> skySections, IReadOnlySet<int> blockSections)
    {
        this.Chunk = chunk;
        for (var index = 0; index < chunk.Sections.Length; index++)
            this.CaptureSection(index, skySections.Contains(index), blockSections.Contains(index));
    }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteVarInt(Chunk.X);
        writer.WriteVarInt(Chunk.Z);

        this.WriteLight(writer);
    }

    /// <summary>Writes the captured masks and arrays, also used by the initial chunk packet.</summary>
    internal void WriteLight(INetStreamWriter writer)
    {
        WriteMask(writer, this.skyMask);
        WriteMask(writer, this.blockMask);
        WriteMask(writer, this.emptySkyMask);
        WriteMask(writer, this.emptyBlockMask);
        WriteArrays(writer, this.sky);
        WriteArrays(writer, this.block);
    }

    private void CaptureSection(int index, bool sky, bool block)
    {
        var section = this.Chunk.Sections[index];
        if (sky)
            Capture(index, section.SkyLightArray, this.skyMask, this.emptySkyMask, this.sky);

        if (block)
            Capture(index, section.BlockLightArray, this.blockMask, this.emptyBlockMask, this.block);
    }

    private static void Capture(
        int index, ReadOnlyMemory<byte> data, BitSet mask, BitSet emptyMask, List<ReadOnlyMemory<byte>> arrays)
    {
        // Protocol bit zero is the section below the build range.
        if (!data.Span.ContainsAnyExcept((byte)0))
        {
            emptyMask.SetBit(index + 1, true);
        }
        else
        {
            mask.SetBit(index + 1, true);

            // Other light is copied: the section's storage changes after the snapshot is taken.
            arrays.Add(data.Length == FullLight.Length && !data.Span.ContainsAnyExcept((byte)0xFF)
                ? FullLight
                : data.ToArray());
        }
    }

    private static ReadOnlyMemory<byte> CreateFullLight()
    {
        var light = new byte[2048];

        // Two levels of 15 a byte.
        Array.Fill(light, (byte)0xFF);
        return light;
    }

    private static void WriteMask(INetStreamWriter writer, BitSet mask)
    {
        writer.WriteVarInt(mask.DataStorage.Length);
        if (mask.DataStorage.Length != 0)
            writer.WriteLongArray(mask.DataStorage.ToArray());
    }

    private static void WriteArrays(INetStreamWriter writer, List<ReadOnlyMemory<byte>> arrays)
    {
        writer.WriteVarInt(arrays.Count);
        foreach (var array in arrays)
        {
            writer.WriteVarInt(array.Length);
            writer.WriteByteArray(array.Span);
        }
    }
}
