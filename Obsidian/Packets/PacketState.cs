//HACK: This might have to be renamed in the coming coding of the server. PlayerState might be the right name. 
public enum PacketState
{
    Handshaking,
    Status = 1,
    Login = 2,
    Play,
}