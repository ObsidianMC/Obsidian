namespace Obsidian.Net;

/// <summary>
/// A connection that can send a level's packet only while its client is in that level, at the packet's place in its
/// output.
/// </summary>
internal interface IConditionalPacketSender
{
    /// <summary>
    /// The connection's level epoch: how many level changes (respawn packets, and the login packet that starts play)
    /// were admitted to its output so far, and the level the last one put the client in.
    /// </summary>
    /// <remarks>
    /// Read it before checking that the player is in the level: a level change sets the player's level before it
    /// queues its respawn, so an epoch read first is never newer than the level seen after it.
    /// </remarks>
    LevelEpoch LevelEpoch { get; }

    /// <summary>
    /// Sends a broadcast's packet like <see cref="IClient.SendPacket"/>, if no level change was admitted since the
    /// epoch numbered <paramref name="levelEpoch"/> was read and <paramref name="wanted"/> still returns true. Both are
    /// checked as the packet is admitted to the output, together with admitting it, so nothing admitted before can make
    /// it stale and anything admitted after follows it. Call it on the broadcasting thread, which owns
    /// <paramref name="shared"/>.
    /// </summary>
    /// <remarks>
    /// <paramref name="wanted"/> runs under the connection's locks, only if the epoch still matches: it must be quick,
    /// and mustn't send or wait. It may record that the packet goes out, as an acknowledgement's watermark does.
    /// </remarks>
    /// <returns>Whether the packet is sent or queued.</returns>
    bool SendPacketIf(SharedPacketBodies shared, int levelEpoch, Func<bool> wanted);
}

/// <summary>
/// The level a connection's client is in from a point of its output on (see
/// <see cref="IConditionalPacketSender.LevelEpoch"/>).
/// </summary>
/// <param name="Number">How many level changes were admitted before this point.</param>
/// <param name="Level">
/// The player's level as the last change was admitted: the level its respawn names, since a transfer sets the level
/// before it queues the respawn. Null before play.
/// </param>
internal sealed record LevelEpoch(int Number, ILevel? Level);
