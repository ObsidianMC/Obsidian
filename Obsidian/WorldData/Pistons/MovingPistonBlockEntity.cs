using Obsidian.API;
using Obsidian.API.BlockEntities;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Registries;
using Obsidian.WorldData.BlockNotifications;
using Obsidian.WorldData.Redstone;

namespace Obsidian.WorldData.Pistons;

/// <summary>
/// The block entity of a moving piston (vanilla <c>PistonMovingBlockEntity</c>). It holds the block being moved and moves
/// toward its destination over two ticks (progress 0.5 per tick). It pushes or carries the entities in its path, and when
/// it finishes, the block it stands for is placed at its position.
/// </summary>
internal sealed class MovingPistonBlockEntity : BlockEntity
{
    /// <summary>Progress added per game tick (vanilla moves a block half a block a tick).</summary>
    public const float ProgressPerTick = 0.5f;

    private IBlock movedState = BlocksRegistry.GetDefault(BlockIds.Air);
    private BlockFace facing = BlockFace.North;
    private bool extending;
    private bool source;
    private float progress;
    private float progressO;
    private long lastTicked;
    private bool finished;
    private IRedstoneLevel? world;

    /// <summary>
    /// Test hook: runs on the ticking thread just before a push re-checks the entity it moves and commits the move.
    /// </summary>
    internal Action<Obsidian.Entities.Entity>? BeforePushCommit { get; set; }

    public MovingPistonBlockEntity()
    {
        this.Id = "minecraft:piston";
    }

    /// <summary>The block that this piston places when it finishes (vanilla <c>movedState</c>).</summary>
    public IBlock MovedState => this.movedState;

    /// <summary>The facing the block moves away from (vanilla <c>direction</c>).</summary>
    public BlockFace Facing => this.facing;

    /// <summary>Whether the block moves out of the piston (extending) or back toward it (retracting).</summary>
    public bool Extending => this.extending;

    /// <summary>Whether this entity is the piston head or base that started the movement (vanilla <c>isSourcePiston</c>).</summary>
    public bool IsSource => this.source;

    /// <summary>The game time this entity last ticked at (vanilla <c>lastTicked</c>).</summary>
    public long LastTicked => this.lastTicked;

    /// <summary>The progress at a partial tick (vanilla <c>getProgress</c>).</summary>
    public float ProgressAt(float partialTick) => this.progressO + (this.progress - this.progressO) * partialTick;

    /// <summary>The direction the moved block travels (vanilla <c>getMovementDirection</c>).</summary>
    public BlockFace MovementDirection => this.extending ? this.facing : this.facing.Opposite();

    /// <summary>Sets what this entity moves. Called once, when the movement starts.</summary>
    internal void Configure(IRedstoneLevel level, IBlock moved, BlockFace facing, bool extending, bool source)
    {
        this.world = level;
        this.movedState = moved;
        this.facing = facing;
        this.extending = extending;
        this.source = source;
        this.progress = 0;
        this.progressO = 0;
        this.finished = false;
    }

    protected internal override void Load(NbtCompound tag)
    {
        this.movedState = tag.TryGetTag("blockState", out var state) ? BlockStateNbt.Read(state) : BlocksRegistry.Air;
        this.facing = tag.TryGetNumber<int>("facing", out var face) && face is >= 0 and <= 5
            ? (BlockFace)face : BlockFace.North;
        this.extending = tag.TryGetBool("extending", out var extend) && extend;
        this.source = tag.TryGetBool("source", out var isSource) && isSource;
        this.progress = tag.TryGetNumber<float>("progress", out var saved) ? saved : 0;
        this.progressO = this.progress;
    }

    protected internal override void Save(NbtCompound tag)
    {
        tag.Add(BlockStateNbt.Write(this.movedState, "blockState"));
        tag.Add(new NbtTag<int>("facing", (int)this.facing));
        tag.Add(new NbtTag<bool>("extending", this.extending));
        tag.Add(new NbtTag<bool>("source", this.source));
        tag.Add(new NbtTag<float>("progress", this.progressO));
    }

    protected internal override NbtCompound? GetUpdateData()
    {
        var tag = new NbtCompound();
        this.Save(tag);
        return tag;
    }

    // Vanilla PistonMovingBlockEntity.tick: a finished entity places its block; otherwise it moves the entities in its
    // path by the progress it advances this tick.
    protected internal override async ValueTask TickAsync()
    {
        var level = this.world ?? this.Level as IRedstoneLevel;
        if (level is null || this.finished)
            return;

        this.lastTicked = level.GameTime;
        this.progressO = this.progress;
        if (this.progressO >= 1f)
        {
            await this.PlaceMovedStateAsync(level, completedMove: true);
            return;
        }

        this.progress = Math.Min(this.progress + ProgressPerTick, 1f);
        await this.MoveEntitiesAsync(level, this.progress - this.progressO);
    }

    /// <summary>
    /// Vanilla <c>finalTick</c>: cuts the move short and places the block the piston stands for now, as another piston
    /// event does to a block still moving. For a source piston vanilla places air instead; Obsidian does not yet
    /// reproduce that source-piston case.
    /// </summary>
    internal ValueTask FinishAsync(IRedstoneLevel level) => this.PlaceMovedStateAsync(level, completedMove: false);

    // Places the moved state, shaped by its new neighbors. A move that ran its course (completedMove) breaks a block
    // that can't stay there, with its drops; one cut short just places what the shape rules leave, air included.
    private async ValueTask PlaceMovedStateAsync(IRedstoneLevel level, bool completedMove)
    {
        if (this.finished)
            return;

        this.finished = true;
        this.progress = 1f;
        this.progressO = 1f;
        var position = this.BlockPosition;
        var current = level.GetLoadedBlock(position);
        if (current is null || !PistonRules.IsMoving(current))
            return;

        // Derivation runs for every moved state: observers schedule their pulse, and fences reconnect even when
        // none of the destination's neighbors changed. The captured piston scenarios cover both effects.
        var placed = NeighborShapeDerivation.Derive(level, position, this.movedState);
        if (completedMove && placed.IsAir)
        {
            // Like vanilla, the moved state is installed first and then broken, so it drops as itself (a carpet
            // pushed over air), as captured in piston_pushes_carpet_off_support.
            await level.RestoreBlockSilentlyAsync(position, this.movedState);
            await level.DestroyBlockNowAsync(position, this.movedState, playEffect: true);
            return;
        }

        await level.WriteAsync(position, placed, notifyNeighbors: true);
    }

    // Vanilla moveCollidedEntities / moveStuckEntities: entities in the moving block's path are pushed along it, and
    // honey carries the entities standing on a retracting honey head.
    private async ValueTask MoveEntitiesAsync(IRedstoneLevel level, float delta)
    {
        var direction = this.MovementDirection;
        var cell = new BoundingBox(
            new VectorD(this.BlockPosition.X, this.BlockPosition.Y, this.BlockPosition.Z),
            new VectorD(this.BlockPosition.X + 1, this.BlockPosition.Y + 1, this.BlockPosition.Z + 1));
        // Vanilla moveByPositionAndProgress: the cell is the destination, so the box lags it by the remaining progress.
        var moving = Shift(cell, direction, this.progress - 1);

        var launches = this.movedState.Is(BlockIds.SlimeBlock);
        foreach (var entity in level.EntitiesIn(moving.Min, moving.Max).ToList())
        {
            if (entity is not Obsidian.Entities.Entity concrete)
                continue;

            if (launches)
                Launch(level, concrete, direction);

            var needed = Distance(moving, direction, entity.BoundingBox);
            if (needed <= 0)
                continue;

            await this.MoveAsync(level, concrete, direction, Math.Min(needed, delta) + 0.01);
        }

        // Vanilla moveStuckEntities: a honey block moving sideways carries the entities on it, whether extending or not.
        if (this.movedState.Is(BlockIds.HoneyBlock) && direction is not (BlockFace.Up or BlockFace.Down))
        {
            var top = Shift(
                new BoundingBox(
                    new VectorD(cell.Min.X, cell.Max.Y, cell.Min.Z),
                    new VectorD(cell.Max.X, cell.Max.Y + 1.5000010000000001, cell.Max.Z)),
                direction,
                this.progress - 1);
            foreach (var entity in level.EntitiesIn(top.Min, top.Max).ToList())
            {
                if (entity is Obsidian.Entities.Entity concrete && entity.BoundingBox.Min.Y >= top.Min.Y - 0.01)
                    await this.MoveAsync(level, concrete, direction, delta);
            }
        }
    }

    // Vanilla moveCollidedEntities: a moving slime block sets the velocity of the entities it meets to one block a
    // tick along its axis, keeping the other components. Players' clients move themselves (vanilla's
    // canSimulateMovement), so only the server's own entities are launched here, and only while they're still in the
    // piston's level: an earlier push's await may have let one travel.
    private static void Launch(IRedstoneLevel level, Obsidian.Entities.Entity entity, BlockFace face)
    {
        if (entity is PlayerBase)
            return;

        if (level is not AbstractLevel owner)
        {
            SetLaunchMotion(entity, face);
            return;
        }

        using (owner.TransferLock.Enter())
        {
            if (entity.Level == owner && entity.IsTravelResident(owner))
                SetLaunchMotion(entity, face);
        }
    }

    private static void SetLaunchMotion(Obsidian.Entities.Entity entity, BlockFace face)
    {
        var step = face.ToVector();
        var motion = entity.Motion;
        entity.Motion = face switch
        {
            BlockFace.East or BlockFace.West => new VectorD(step.X, motion.Y, motion.Z),
            BlockFace.Up or BlockFace.Down => new VectorD(motion.X, step.Y, motion.Z),
            _ => new VectorD(motion.X, motion.Y, step.Z)
        };
    }

    private static BoundingBox Shift(BoundingBox box, BlockFace face, float amount)
    {
        var delta = face.ToVector();
        var offset = new VectorD(delta.X * amount, delta.Y * amount, delta.Z * amount);
        return new BoundingBox(box.Min + offset, box.Max + offset);
    }

    // Vanilla getMovement: how far an entity must travel along the face so its box clears the moving block's box.
    private static double Distance(BoundingBox moving, BlockFace face, BoundingBox entity) => face switch
    {
        BlockFace.East => moving.Max.X - entity.Min.X,
        BlockFace.West => entity.Max.X - moving.Min.X,
        BlockFace.Up => moving.Max.Y - entity.Min.Y,
        BlockFace.Down => entity.Max.Y - moving.Min.Y,
        BlockFace.South => moving.Max.Z - entity.Min.Z,
        _ => entity.Max.Z - moving.Min.Z,
    };

    // Moves the entity along the face. The pushes before it await, so it may have travelled to another level or moved
    // since it was found: it's moved only while it's still where this push saw it in the piston's level, re-checked
    // under that level's transfer lock, which travel commits under too.
    private async ValueTask MoveAsync(
        IRedstoneLevel level,
        Obsidian.Entities.Entity entity,
        BlockFace face,
        double amount)
    {
        var delta = face.ToVector();
        var observedPosition = entity.Position;
        var position = observedPosition + new VectorD(delta.X * amount, delta.Y * amount, delta.Z * amount);
        if (level is not AbstractLevel owner)
        {
            await entity.UpdatePositionAsync(position, MovementFlags.OnGround);
            return;
        }

        var (chunkX, chunkZ) = Region.ChunkOf(position);
        await owner.GetChunkAsync(chunkX, chunkZ, scheduleGeneration: false);

        this.BeforePushCommit?.Invoke(entity);

        Task movement;
        lock (owner.Fluids.TickLock)
        {
            using (owner.TransferLock.Enter())
            {
                if (entity.Level != owner || entity.Position != observedPosition || !entity.IsTravelResident(owner))
                    return;

                if (owner.GetLoadedChunk(chunkX, chunkZ) is null)
                    return;

                // The tick lock keeps the chunk loaded, so UpdatePositionAsync's chunk lookup completes synchronously:
                // the position, grid membership and box change before the transfer lock is released. Await outside it.
                movement = entity.UpdatePositionAsync(position, MovementFlags.OnGround);
            }
        }

        await movement;
    }
}
