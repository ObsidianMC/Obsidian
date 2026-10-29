namespace Obsidian;

public sealed partial class Server
{
    private readonly System.Threading.Lock sessionLock = new();
    private Guid? sessionId;

    /// <summary>
    /// The id Login Success gives clients for the server's session (26.2). Like vanilla's, every client connected at
    /// the same time gets the same id; a server that had no connections for a tick starts a new session.
    /// </summary>
    internal Guid SessionId
    {
        get
        {
            lock (this.sessionLock)
                return this.sessionId ??= Guid.NewGuid();
        }
    }

    // Ends the session once nobody is connected, as vanilla does each tick.
    private void EndIdleSession()
    {
        if (!this.Connections.IsEmpty)
            return;

        lock (this.sessionLock)
            this.sessionId = null;
    }
}
