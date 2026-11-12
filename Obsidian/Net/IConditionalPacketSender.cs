namespace Obsidian.Net;

/// <summary>
/// A connection that can send a level's packet only while its player is still in that level, at the packet's place in
/// its output.
/// </summary>
internal interface IConditionalPacketSender
{
    /// <summary>
    /// The connection's level epoch: how many level changes (respawn packets, and the login packet that starts play)
    /// were admitted to its output so far. A packet admitted at the same epoch reaches the client in the same level.
    /// </summary>
    /// <remarks>
    /// Read it before checking that the player is in the level: a level change sets the player's level before it
    /// queues its respawn, so an epoch read first is never newer than the level seen after it.
    /// </remarks>
    int LevelEpoch { get; }

    /// <summary>
    /// Sends a broadcast's packet like <see cref="IClient.SendPacket"/>, if no level change was admitted since
    /// <paramref name="levelEpoch"/> was read and <paramref name="wanted"/> still returns true. Both are checked as the
    /// packet is admitted to the output, together with admitting it, so nothing admitted before can make it stale and
    /// anything admitted after follows it. Call it on the broadcasting thread, which owns <paramref name="shared"/>.
    /// </summary>
    /// <remarks>
    /// <paramref name="wanted"/> runs under the connection's locks, only if the epoch still matches: it must be quick,
    /// and mustn't send or wait. It may record that the packet goes out, as an acknowledgement's watermark does.
    /// </remarks>
    /// <returns>Whether the packet is sent or queued.</returns>
    bool SendPacketIf(SharedPacketBodies shared, int levelEpoch, Func<bool> wanted);
}
