namespace Obsidian.API;

/// <summary>Kinds of server events.</summary>
public enum EventType
{
    /// <summary>A packet was received from a client.</summary>
    PacketReceived,
    /// <summary>A packet was queued to be sent to a client.</summary>
    QueuePacket,
    /// <summary>A player joined the server.</summary>
    PlayerJoin,
    /// <summary>A player left the server.</summary>
    PlayerLeave,
    /// <summary>A player was teleported.</summary>
    PlayerTeleported,
    /// <summary>A player was granted a permission.</summary>
    PermissionGranted,
    /// <summary>A permission was revoked from a player.</summary>
    PermissionRevoked,
    /// <summary>A player clicked a slot in a container.</summary>
    ContainerClick,
    /// <summary>A block was broken.</summary>
    BlockBreak,
    /// <summary>A player sent a chat message.</summary>
    IncomingChatMessage,
    /// <summary>A client asked for the server list status.</summary>
    ServerStatusRequest,
    /// <summary>A player interacted with an entity.</summary>
    EntityInteract,
    /// <summary>A player attacked an entity.</summary>
    PlayerAttackEntity,
    /// <summary>A player used an item or clicked a block.</summary>
    PlayerInteract,
    /// <summary>A player closed a container.</summary>
    ContainerClosed,
    /// <summary>An event a plugin defines.</summary>
    Custom
}
