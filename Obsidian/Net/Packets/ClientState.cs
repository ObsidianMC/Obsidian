namespace Obsidian.Net.Packets;

public enum ClientState
{
    Handshaking = 0,
    Status = 1,
    Login = 2,
    Play = 3,
    Closed = -1,
}
