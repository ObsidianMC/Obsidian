namespace Obsidian.Net.Packets.Play.Clientbound;

/// <summary>
/// Vanilla's <c>ClientboundSectionBlocksUpdatePacket</c>: several changed blocks of one chunk section, each with its
/// new state. Levels send it once a tick for a section where more than one block changed (see
/// <see cref="WorldData.BlockChangeBroadcasts"/>).
/// </summary>
/// <param name="sectionX">The section's chunk X.</param>
/// <param name="sectionY">The section's Y: the blocks' Y divided by 16, rounding down.</param>
/// <param name="sectionZ">The section's chunk Z.</param>
/// <param name="changes">
/// The changed blocks, each packed by <see cref="PackChange"/>, in the order they're sent.
/// </param>
public partial class SectionBlocksUpdatePacket(int sectionX, int sectionY, int sectionZ, long[] changes)
{
    public int SectionX { get; } = sectionX;

    public int SectionY { get; } = sectionY;

    public int SectionZ { get; } = sectionZ;

    /// <summary>The changed blocks, each packed by <see cref="PackChange"/>.</summary>
    public long[] Changes { get; } = changes;

    /// <summary>
    /// Packs a changed block as vanilla sends it: the state's id shifted left by 12 bits, then the block's position
    /// in its section, <c>x &lt;&lt; 8 | z &lt;&lt; 4 | y</c>.
    /// </summary>
    public static long PackChange(int stateId, short positionInSection) =>
        (long)stateId << 12 | (ushort)positionInSection;

    /// <summary>
    /// A block's position in its section, packed like vanilla's <c>SectionPos.sectionRelativePos</c>.
    /// </summary>
    public static short PackPosition(int x, int y, int z) => (short)((x & 15) << 8 | (z & 15) << 4 | (y & 15));

    /// <summary>The changed blocks' positions and state ids, in the order they're sent.</summary>
    public IEnumerable<(Vector Position, int StateId)> Blocks()
    {
        foreach (var change in this.Changes)
        {
            var x = (int)(change >> 8 & 15);
            var z = (int)(change >> 4 & 15);
            var y = (int)(change & 15);
            var position = new Vector(this.SectionX << 4 | x, this.SectionY << 4 | y, this.SectionZ << 4 | z);
            yield return (position, (int)(change >>> 12));
        }
    }

    // Vanilla's SectionPos.asLong: 22 bits of X, then 22 of Z, then 20 of Y.
    public override void Serialize(INetStreamWriter writer)
    {
        var section = ((long)this.SectionX & 0x3FFFFF) << 42
            | ((long)this.SectionZ & 0x3FFFFF) << 20
            | ((long)this.SectionY & 0xFFFFF);
        writer.WriteLong(section);

        writer.WriteVarInt(this.Changes.Length);
        foreach (var change in this.Changes)
            writer.WriteVarLong(change);
    }
}
