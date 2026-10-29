using System.Buffers.Binary;

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

    /// <summary>The most bytes <see cref="WriteLight"/> writes, for sizing a buffer ahead.</summary>
    internal int MaxLightSize()
    {
        // Each mask is a VarInt length, then at most its longs' bytes; each list of arrays a VarInt count, then each array's
        // VarInt length and its bytes. A VarInt takes at most 5 bytes.
        var size = 6 * 5;
        size += sizeof(long) * (this.skyMask.DataStorage.Length + this.blockMask.DataStorage.Length
            + this.emptySkyMask.DataStorage.Length + this.emptyBlockMask.DataStorage.Length);

        foreach (var array in this.sky)
            size += 5 + array.Length;

        foreach (var array in this.block)
            size += 5 + array.Length;

        return size;
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

    /// <summary>
    /// Writes a section mask like vanilla's <c>ByteBufCodecs.BIT_SET</c> (26.3): a VarInt length, then the mask's bytes,
    /// low bits first, without the trailing zero bytes (Java's <c>BitSet.toByteArray</c>).
    /// </summary>
    internal static void WriteMask(INetStreamWriter writer, BitSet mask)
    {
        var longs = mask.DataStorage.Span;
        Span<byte> bytes = stackalloc byte[longs.Length * sizeof(long)];
        for (var i = 0; i < longs.Length; i++)
            BinaryPrimitives.WriteInt64LittleEndian(bytes[(i * sizeof(long))..], longs[i]);

        var length = bytes.LastIndexOfAnyExcept((byte)0) + 1;
        writer.WriteVarInt(length);
        writer.WriteByteArray(bytes[..length]);
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
