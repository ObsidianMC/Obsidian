namespace Obsidian.Net;

/// <summary>
/// A packet broadcast to several connections, serialized (see <see cref="NetworkBuffer.SerializeBodies"/>) once for all
/// the connections that would write it alike instead of once for each. Make one per broadcast and drop it after, on the
/// broadcasting thread: it isn't thread-safe, and what it serialized stays as the packet was then.
/// </summary>
/// <remarks>
/// Connections write a body alike unless they differ in <see cref="NetworkBuffer.WritesPluginEntityData"/>, so it keeps
/// one set of bodies for each; bodies are written without <see cref="NetworkBuffer.IdMaps"/>. Anything else that makes
/// serializing connection-specific must become part of the key here. The connections only read the bodies: framing
/// copies them into each connection's own output.
/// </remarks>
internal sealed class SharedPacketBodies(IClientboundPacket packet)
{
    private List<NetworkBuffer>? bodies;
    private List<NetworkBuffer>? bodiesWithPluginEntityData;

    public IClientboundPacket Packet { get; } = packet;

    /// <summary>
    /// The packet's bodies for connections that do or don't read plugins' entity data, serialized the first time
    /// they're asked for.
    /// </summary>
    public IReadOnlyList<NetworkBuffer> Get(bool writesPluginEntityData)
    {
        ref var cached = ref writesPluginEntityData ? ref this.bodiesWithPluginEntityData : ref this.bodies;
        if (cached is null)
        {
            // Kept only once complete, so a packet that fails to serialize fails again for the next connection.
            var serialized = new List<NetworkBuffer>(1);
            NetworkBuffer.SerializeBodies(this.Packet, writesPluginEntityData, serialized);
            cached = serialized;
        }

        return cached;
    }
}
