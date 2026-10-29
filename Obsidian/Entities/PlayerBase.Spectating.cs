using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.WorldData;

namespace Obsidian.Entities;

public partial class PlayerBase
{
    /// <summary>
    /// The entity a spectator watches through, or null while they see through their own eyes.
    /// </summary>
    internal IEntity? Camera { get; private set; }

    /// <summary>
    /// Has a spectator watch through <paramref name="entity"/>'s eyes, or through their own with null, like vanilla's
    /// <c>ServerPlayer.setCamera</c>. Taking another entity's view moves the player to it.
    /// </summary>
    internal async ValueTask SetCameraAsync(IEntity? entity)
    {
        if (ReferenceEquals(entity, this))
            entity = null;
        if (ReferenceEquals(entity, this.Camera))
            return;

        this.Camera = entity;
        await this.QueuePacketAsync(new SetCameraPacket { CameraId = (entity ?? this).EntityId });
        if (entity is not null)
            await this.TeleportAsync(entity.Level, entity.Position, entity.Yaw, entity.Pitch);
    }

    // Vanilla's ServerPlayer.tick: a spectator follows the entity they watch, and leaves it when they sneak, stop
    // spectating or it goes away.
    private async ValueTask TickCameraAsync()
    {
        if (this.Camera is not { } camera)
            return;

        if (this.Sneaking || this.GameMode != GameMode.Spectator || camera.Level != this.Level ||
            camera is ILiving { Alive: false } ||
            this.Level is AbstractLevel level && level.FindEntity(camera.Uuid) is null)
        {
            await this.SetCameraAsync(null);
            return;
        }

        // The client draws from the camera; the server only keeps the player, and the chunks they load, with it.
        await this.UpdateAsync(camera.Position, this.MovementFlags);
        if (this.Position.ToChunkCoord() != this.LastPosition.ToChunkCoord())
            await this.UpdateChunksAsync();
        this.LastPosition = this.Position;
    }
}
