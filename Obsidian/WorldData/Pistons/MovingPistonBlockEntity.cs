using Obsidian.API;
using Obsidian.API.BlockEntities;
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
        this.movedState = BlocksRegistry.Get(tag.TryGetTagValue<int>("MovedState", out var state) ? state : BlockIds.Air);
        this.facing = Enum.TryParse<BlockFace>(tag.TryGetTagValue<string>("Facing", out var face) ? face : null, true, out var parsed)
            ? parsed
            : BlockFace.North;
        this.extending = tag.TryGetTagValue<bool>("Extending", out var extend) && extend;
        this.source = tag.TryGetTagValue<bool>("Source", out var isSource) && isSource;
        this.progress = tag.TryGetTagValue<float>("Progress", out var saved) ? saved : 0;
        this.progressO = this.progress;
    }

    protected internal override void Save(NbtCompound tag)
    {
        // The full state id, so properties such as facing or axis survive a reload.
        tag.Add(new NbtTag<int>("MovedState", this.movedState.GetHashCode()));
        tag.Add(new NbtTag<string>("Facing", this.facing.ToString().ToLowerInvariant()));
        tag.Add(new NbtTag<bool>("Extending", this.extending));
        tag.Add(new NbtTag<bool>("Source", this.source));
        tag.Add(new NbtTag<float>("Progress", this.progress));
    }

    // Vanilla getUpdateTag: the client rebuilds the moving block from its blockState, facing and progress.
    protected internal override NbtCompound? GetUpdateData()
    {
        var state = new NbtCompound("blockState") { new NbtTag<string>("Name", this.movedState.UnlocalizedName) };
        var properties = this.movedState.GetProperties();
        if (properties.Count > 0)
        {
            var values = new NbtCompound("Properties");
            foreach (var (name, value) in properties)
                values.Add(new NbtTag<string>(name, value));

            state.Add(values);
        }

        var tag = new NbtCompound
        {
            state,
            new NbtTag<string>("facing", this.facing.ToString().ToLowerInvariant()),
            new NbtTag<float>("progress", this.progress),
            new NbtTag<bool>("extending", this.extending),
            new NbtTag<bool>("source", this.source)
        };
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
            await this.FinishAsync(level);
            return;
        }

        this.progress = Math.Min(this.progress + ProgressPerTick, 1f);
        await this.MoveEntitiesAsync(level, this.progress - this.progressO);
    }

    /// <summary>Vanilla <c>finalTick</c>: places the block the piston stands for, or air for a source that retracts.</summary>
    internal async ValueTask FinishAsync(IRedstoneLevel level)
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

        // The moved state is placed (the extended head, the retracted base, or a pushed block). Checked against the
        // vanilla oracle: a finished retraction leaves the base in place.
        await level.WriteAsync(position, this.movedState, notifyNeighbors: true);
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

        foreach (var entity in level.EntitiesIn(moving.Min, moving.Max).ToList())
        {
            if (entity is not Obsidian.Entities.Entity concrete)
                continue;

            var needed = Distance(moving, direction, entity.BoundingBox);
            if (needed <= 0)
                continue;

            await Move(concrete, direction, Math.Min(needed, delta) + 0.01);
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
                    await Move(concrete, direction, delta);
            }
        }
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

    private static async ValueTask Move(Obsidian.Entities.Entity entity, BlockFace face, double amount)
    {
        var delta = face.ToVector();
        var position = entity.Position + new VectorD(delta.X * amount, delta.Y * amount, delta.Z * amount);
        await entity.UpdatePositionAsync(position, MovementFlags.OnGround);
    }
}
