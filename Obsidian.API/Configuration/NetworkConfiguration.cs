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

    public long KeepAliveInterval { get; set; } = 10_000;

    public long KeepAliveTimeoutInterval { get; set; } = 30_000;

    /// <summary>
    /// The time in milliseconds to wait before an ip is allowed to try and connect again.
    /// </summary>
    public long ConnectionThrottle { get; set; } = 15_000;

    /// <summary>
    /// If true, each login/client gets a random username where multiple connections from the same host will be allowed.
    /// </summary>
    public bool MulitplayerDebugMode { get; set; } = false;
}
