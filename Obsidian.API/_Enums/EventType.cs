namespace Obsidian.API;

public enum EventType
{
    PacketReceived,
    QueuePacket,
    PlayerJoin,
    PlayerLeave,
    PlayerTeleported,
    PermissionGranted,
    PermissionRevoked,
    ContainerClick,
    BlockBreak,
    IncomingChatMessage,
    ServerStatusRequest,
    EntityInteract,
    PlayerAttackEntity,
    PlayerInteract,
    ContainerClosed,
    Custom
}
