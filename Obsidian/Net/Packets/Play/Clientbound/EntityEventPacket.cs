using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

/// <summary>
/// Vanilla's entity event: an entity id and an event byte whose meaning depends on the entity.
/// </summary>
public partial class EntityEventPacket
{
    // Vanilla's EntityEvent.PERMISSION_LEVEL_ALL: a player's own permission level 0-4 is sent as this plus the level.
    private const byte PermissionLevelBase = 24;

    [Field(0)]
    public required int EntityId { get; init; }

    [Field(1)]
    public required byte Event { get; init; }

    /// <summary>
    /// Tells a player their permission level (0 to 4), which the client uses to show operator-only commands and options.
    /// </summary>
    public static EntityEventPacket PermissionLevel(int entityId, int level) =>
        new() { EntityId = entityId, Event = (byte)(PermissionLevelBase + Math.Clamp(level, 0, 4)) };

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteInt(this.EntityId);
        writer.WriteByte(this.Event);
    }
}
