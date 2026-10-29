namespace Obsidian.Net.Packets.Play.Clientbound;

/// <summary>
/// The wire forms 26.3 gives entity moves: <c>VecDelta</c> in the relative-move packets and <c>PositionPath</c> in
/// <see cref="EntityPositionSyncPacket"/>.
/// </summary>
/// <remarks>
/// Both have a stepped form, which replays several ticks of movement at once. Vanilla sends it for living entities'
/// per-tick history between updates; Obsidian sends one position per update, so it only writes the linear forms.
/// </remarks>
internal static class EntityMoveDelta
{
    // VecDelta's packed header: bit 0 is the ground flag, the bits above it the step count (0 for a linear move).
    private const int OnGroundFlag = 1;

    // PositionPath's type ids.
    private const int LinearPath = 0;

    /// <summary>
    /// Writes a relative move's header and its linear <c>VecDelta</c>: the move, in 1/4096 blocks, as three shorts.
    /// </summary>
    public static void Write(INetStreamWriter writer, Vector delta, bool onGround)
    {
        writer.WriteVarInt(onGround ? OnGroundFlag : 0);
        writer.WriteAbsoluteShortPosition(delta);
    }

    /// <summary>
    /// Writes a linear <c>PositionPath</c>, which ends at <paramref name="position"/>.
    /// </summary>
    public static void WritePath(INetStreamWriter writer, VectorD position)
    {
        writer.WriteVarInt(LinearPath);
        VectorD.Write(position, writer);
    }
}
