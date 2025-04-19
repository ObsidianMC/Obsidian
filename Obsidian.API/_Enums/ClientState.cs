namespace Obsidian.API;
public enum ClientState
{
    Handshaking = 0,
    Status = 1,
    Login = 2,
    Configuration = 3,
    Play = 4,
    Closed = -1,
}
