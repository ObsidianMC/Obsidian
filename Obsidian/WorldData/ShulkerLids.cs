using System.Threading;
using Obsidian.API.Blocks;
using Obsidian.Entities;
using Obsidian.Entities.AI;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData;

/// <summary>
/// Vanilla's shulker box lids: the room a closed box needs before it opens, and the box an opening or open lid fills
/// (<c>Shulker.getProgressAabb</c> and <c>getProgressDeltaAabb</c>). The lid moves the way the box faces, not up.
/// </summary>
internal static class ShulkerLids
{
    /// <summary>How far an open lid reaches out of its block: half a block.</summary>
    internal const float MaxLidHeight = 0.5f;

    /// <summary>
    /// Vanilla's <c>ShulkerBoxBlock.canOpen</c>: a box whose lid is already moving opens; a closed one only when the
    /// half block in front of its lid has nothing it would collide with (blocks, and entities others collide with).
    /// </summary>
    internal static bool CanOpen(AbstractLevel level, IBlock block, Vector position)
    {
        if (level.GetResidentBlockEntity(position) is ShulkerBoxBlockEntity { Lid: not ShulkerLidStatus.Closed })
            return true;

        var room = Deflate(DeltaBox(Facing(block), 0, MaxLidHeight, position), 1.0E-6);
        return new MobTerrain(level).IsFree(room) && !BlockedByEntities(level, room);
    }

    /// <summary>
    /// The collision box of a shulker box whose lid is <paramref name="progress"/> open (0 to 1), relative to its
    /// block: the block grown half a block at most the way it faces. Null for a closed box, which is a whole block.
    /// </summary>
    internal static BoundingBox? CollisionBox(IBlock block, float progress)
    {
        if (progress <= 0)
            return null;

        return DeltaBox(Facing(block), -1, MaxLidHeight * progress, Vector.Zero);
    }

    /// <summary>The face a shulker box's lid opens towards.</summary>
    internal static BlockFace Facing(IBlock block) => FeatureHelpers.ParseFace(block.GetProperty("facing"));

    /// <summary>
    /// Vanilla's <c>Shulker.getProgressDeltaAabb</c> for a whole block standing at <paramref name="position"/>: the
    /// part of the space in front of it that a lid moving from <paramref name="from"/> to <paramref name="to"/> sweeps.
    /// With <paramref name="from"/> at -1, the block itself grown by <paramref name="to"/>.
    /// </summary>
    internal static BoundingBox DeltaBox(BlockFace facing, double from, double to, Vector position)
    {
        var step = facing.ToVector();
        var most = Math.Max(from, to);
        var least = Math.Min(from, to);

        // Vanilla's expandTowards by the furthest the lid reaches, then contract back to where it started.
        var min = new VectorD(0, 0, 0);
        var max = new VectorD(1, 1, 1);
        (min, max) = Expand(min, max, new VectorD(step.X * most, step.Y * most, step.Z * most));
        (min, max) = Contract(min, max, new VectorD(
            -step.X * (1 + least),
            -step.Y * (1 + least),
            -step.Z * (1 + least)));

        var offset = (VectorD)position;
        return new BoundingBox(min + offset, max + offset);
    }

    private static BoundingBox Deflate(BoundingBox box, double amount) =>
        new(box.Min + new VectorD(amount, amount, amount), box.Max - new VectorD(amount, amount, amount));

    // Vanilla's AABB.expandTowards: a negative amount moves the minimum, a positive one the maximum.
    private static (VectorD Min, VectorD Max) Expand(VectorD min, VectorD max, VectorD amount) =>
        (new VectorD(
            amount.X < 0 ? min.X + amount.X : min.X,
            amount.Y < 0 ? min.Y + amount.Y : min.Y,
            amount.Z < 0 ? min.Z + amount.Z : min.Z),
        new VectorD(
            amount.X > 0 ? max.X + amount.X : max.X,
            amount.Y > 0 ? max.Y + amount.Y : max.Y,
            amount.Z > 0 ? max.Z + amount.Z : max.Z));

    // Vanilla's AABB.contract: a negative amount raises the minimum, a positive one lowers the maximum.
    private static (VectorD Min, VectorD Max) Contract(VectorD min, VectorD max, VectorD amount) =>
        (new VectorD(
            amount.X < 0 ? min.X - amount.X : min.X,
            amount.Y < 0 ? min.Y - amount.Y : min.Y,
            amount.Z < 0 ? min.Z - amount.Z : min.Z),
        new VectorD(
            amount.X > 0 ? max.X - amount.X : max.X,
            amount.Y > 0 ? max.Y - amount.Y : max.Y,
            amount.Z > 0 ? max.Z - amount.Z : max.Z));

    // Vanilla's entity collisions in Level.noCollision: what other entities collide with, like boats and shulkers.
    private static bool BlockedByEntities(AbstractLevel level, BoundingBox room) =>
        level.EntitiesIn(room.Min, room.Max).Any(entity => entity is Boat or Shulker { Health: > 0 });

    /// <summary>
    /// Vanilla's <c>Entity.getPistonPushReaction</c> is <c>IGNORE</c>: what neither pistons nor shulker lids move.
    /// </summary>
    internal static bool IgnoresPushing(Entity entity) =>
        entity is ArmorStand { Marker: true } or HangingEntity or AreaEffectCloud or DisplayEntity or Interaction;
}

/// <summary>Vanilla's <c>ShulkerBoxBlockEntity.AnimationStatus</c>.</summary>
internal enum ShulkerLidStatus
{
    Closed,
    Opening,
    Opened,
    Closing
}

internal sealed partial class ShulkerBoxBlockEntity
{
    // Vanilla's OPENING_TICK_LENGTH: the lid moves a tenth of the way each tick.
    private const float ProgressPerTick = 0.1f;

    private volatile ShulkerLidStatus lid;
    private float progress;
    private float progressOld;

    // Test-only interleaving point, after observing the entity and before committing its push.
    internal Action<Entity>? BeforePushCommit { get; set; }

    /// <summary>Whether the lid is closed, opening, open or closing. Not saved, as vanilla's isn't.</summary>
    internal ShulkerLidStatus Lid => this.lid;

    /// <summary>How far open the lid is, from 0 to 1.</summary>
    internal float LidProgress => Volatile.Read(ref this.progress);

    /// <summary>
    /// Vanilla's <c>triggerEvent(1, count)</c>, when the lid's block event runs: no viewers start it closing and the
    /// first viewer starts it opening; other counts leave it moving as it was.
    /// </summary>
    internal void ViewersChanged(int count)
    {
        if (count == 0)
            this.lid = ShulkerLidStatus.Closing;
        else if (count == 1)
            this.lid = ShulkerLidStatus.Opening;
    }

    // Vanilla's updateAnimation, on the server as on clients: the lid moves, telling its neighbours as it starts and
    // stops, and while opening pushes what's in its way. Floats, as vanilla's, decide on which tick it's done.
    protected internal override async ValueTask TickAsync()
    {
        if (this.Level is not AbstractLevel level)
            return;

        this.progressOld = this.progress;
        switch (this.lid)
        {
            case ShulkerLidStatus.Closed:
                Volatile.Write(ref this.progress, 0);
                break;
            case ShulkerLidStatus.Opening:
                await this.OpenFurtherAsync(level);
                break;
            case ShulkerLidStatus.Opened:
                Volatile.Write(ref this.progress, 1);
                break;
            case ShulkerLidStatus.Closing:
                this.CloseFurther(level);
                break;
        }
    }

    private async ValueTask OpenFurtherAsync(AbstractLevel level)
    {
        Volatile.Write(ref this.progress, this.progress + ProgressPerTick);
        if (this.progressOld == 0)
            this.UpdateNeighbors(level);

        if (this.progress >= 1)
        {
            this.lid = ShulkerLidStatus.Opened;
            Volatile.Write(ref this.progress, 1);
            this.UpdateNeighbors(level);
        }

        await this.PushEntitiesAsync(level);
    }

    private void CloseFurther(AbstractLevel level)
    {
        Volatile.Write(ref this.progress, this.progress - ProgressPerTick);
        if (this.progressOld == 1)
            this.UpdateNeighbors(level);

        if (this.progress <= 0)
        {
            this.lid = ShulkerLidStatus.Closed;
            Volatile.Write(ref this.progress, 0);
            this.UpdateNeighbors(level);
        }
    }

    // Vanilla's doNeighborUpdates: shape updates (with every flag of an ordinary update), then neighbour updates.
    private void UpdateNeighbors(AbstractLevel level)
    {
        var position = this.BlockPosition;
        var state = level.BlockUpdates.GetBlock(position);
        level.BlockUpdates.UpdateNeighborShapes(position, state, BlockUpdateFlags.All);
        level.BlockUpdates.UpdateNeighbors(position, state);
    }

    // Vanilla's moveCollidedEntities: what the lid swept this tick moves out of its way, by the sweep's depth and a
    // hundredth, colliding with blocks as it goes. Players' clients move them themselves, as they tick the lid too.
    private async ValueTask PushEntitiesAsync(AbstractLevel level)
    {
        var state = level.BlockUpdates.GetBlock(this.BlockPosition);
        if (!TagsRegistry.Block.ShulkerBoxes.Entries.Contains(state.RegistryId))
            return;

        var facing = ShulkerLids.Facing(state);
        var swept = ShulkerLids.DeltaBox(facing, this.progressOld, this.progress, this.BlockPosition);
        var step = facing.ToVector();
        var size = swept.Max - swept.Min;
        var push = new VectorD(
            (size.X + 0.01) * step.X,
            (size.Y + 0.01) * step.Y,
            (size.Z + 0.01) * step.Z);

        var terrain = new MobTerrain(level);
        foreach (var entity in level.EntitiesIn(swept.Min, swept.Max).OfType<Entity>().ToArray())
        {
            if (entity is IPlayer || ShulkerLids.IgnoresPushing(entity))
                continue;

            var observedPosition = entity.Position;
            var box = entity.CurrentBox;
            var movementFlags = entity.MovementFlags;
            var moved = EntityMovement.Collide(terrain, box, push, stepHeight: 0, grounded: false);
            var destination = observedPosition + moved;
            var (chunkX, chunkZ) = Region.ChunkOf(destination);
            await level.GetChunkAsync(chunkX, chunkZ, scheduleGeneration: false);

            this.BeforePushCommit?.Invoke(entity);

            Task movement;
            lock (level.Fluids.TickLock)
            {
                using (level.TransferLock.Enter())
                {
                    if (entity.Level != level || entity.Position != observedPosition || !entity.IsTravelResident(level))
                        continue;

                    if (level.GetLoadedChunk(chunkX, chunkZ) is null)
                        continue;

                    // TickLock keeps this chunk loaded, so UpdatePositionAsync's chunk lookup completes synchronously.
                    // Position, grid membership and bounding box change before transfer is released; await outside it.
                    movement = entity.UpdatePositionAsync(destination, movementFlags);
                }
            }

            await movement;
        }
    }
}
