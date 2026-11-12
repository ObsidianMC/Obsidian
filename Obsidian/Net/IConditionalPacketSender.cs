namespace Obsidian.Net;

/// <summary>
/// A connection that can send a packet only while a condition still holds where the packet falls in its output.
/// </summary>
internal interface IConditionalPacketSender
{
    /// <summary>
    /// Sends a broadcast's packet like <see cref="IClient.SendPacket"/>, if <paramref name="wanted"/> still returns
    /// true at the packet's place in the connection's output: checked and sent together, so a packet sent or queued
    /// before the check can't make it stale, and one queued after is sent after it. Call it on the broadcasting thread,
    /// which owns <paramref name="shared"/>.
    /// </summary>
    /// <remarks>
    /// <paramref name="wanted"/> may run under the connection's locks: it must only read, and quickly.
    /// </remarks>
    /// <returns>Whether the packet is sent, queued, or waits for its check.</returns>
    bool SendPacketIf(SharedPacketBodies shared, Func<bool> wanted);
}
