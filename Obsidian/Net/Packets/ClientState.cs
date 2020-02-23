//HACK: This might have to be renamed in the coming coding of the server. PlayerState might be the right name. 
namespace Obsidian.Net.Packets
{
    public enum ClientState
    {
        Handshaking = 0,
        Status = 1,
        Login = 2,
        Play = 3,
        //See How_to_Write_a_Server
        Closed = -1,
    }
}