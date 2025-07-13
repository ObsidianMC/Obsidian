namespace Obsidian;
public static class ServerConstants
{
    public const ProtocolVersion DefaultProtocol = ProtocolVersion.v1_21_7;

    public static readonly string ProtocolDescription = DefaultProtocol.GetDescription();

    public const int MaxPayloadLength = 1048576;

    public const string PersistentDataPath = "persistentdata";
    public const string PermissionPath = "permissions";
}
