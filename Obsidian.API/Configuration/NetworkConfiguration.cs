namespace Obsidian.API.Configuration;
public sealed record class NetworkConfiguration
{
    /// <summary>
    /// Returns true if <see cref="ConnectionThrottle"/> has a value greater than 0.
    /// </summary>
    public bool ShouldThrottle => this.ConnectionThrottle > 0;

    /// <summary>
    /// The max amount of bytes that can be sent to the client before compression is required.
    /// </summary>
    public int CompressionThreshold { get; set; } = 256;

    /// <summary>
    /// The zlib level compressed packets are written at: 1 (fastest) to 9 (smallest), 0 to store them uncompressed,
    /// or -1 for zlib's default (6), which vanilla uses. Lower levels spend less CPU on each packet and send more bytes.
    /// Values outside -1 to 9 are clamped. Applies to connections that join after it changes.
    /// </summary>
    public int CompressionLevel { get; set; } = -1;

    public long KeepAliveInterval { get; set; } = 10_000;

    public long KeepAliveTimeoutInterval { get; set; } = 30_000;

    /// <summary>
    /// The time in milliseconds to wait before an ip is allowed to try and connect again.
    /// </summary>
    public long ConnectionThrottle { get; set; } = 15_000;

    /// <summary>
    /// If true, each login/client gets a random username where multiple connections from the same host will be allowed.
    /// </summary>
    public bool MulitplayerDebugMode { get; set; }
}
