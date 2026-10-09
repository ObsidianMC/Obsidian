namespace Obsidian.Entities;

[MinecraftEntity("minecraft:falling_block")]
public sealed partial class FallingBlock : Entity
{
    // Vanilla: each tick the downward speed drops by this much, then is scaled by the drag.
    private const double Gravity = 0.04;
    private const double AirDrag = 0.98;

    public required IBlock Block { get; init; }

    public VectorD SpawnPosition { get; private set; }

    // Blocks per tick, negative is down.
    private double VelocityY;

    private readonly HashSet<Vector> checkedBlocks = [];

    public FallingBlock(VectorD position) : base()
    {
        SpawnPosition = position;
        LastPosition = position;
        Position = position;
        VelocityY = 0;
    }

    public async override ValueTask TickAsync()
    {
        LastPosition = Position;

        VelocityY -= Gravity;
        var next = new VectorD(Position.X, Position.Y + VelocityY, Position.Z);

        // Sends the move to clients, so they follow the fall instead of seeing it jump to the landing spot.
        await UpdateAsync(next, MovementFlags.None);

        // Check below to see if we're about to hit a solid block.
        var upcomingBlockPos = new Vector(
            (int)Math.Floor(next.X),
            (int)Math.Floor(next.Y - 1),
            (int)Math.Floor(next.Z));

        if (checkedBlocks.Add(upcomingBlockPos))
        {
            var upcomingBlock = await Level.GetBlockAsync(upcomingBlockPos);
            if (upcomingBlock is not null && !upcomingBlock.IsFreeForFallingBlock())
            {
                await ConvertToBlock(upcomingBlockPos + Vector.Up);
            }
        }

        VelocityY *= AirDrag;
    }

    private async Task ConvertToBlock(Vector loc)
    {
        await Level.SetBlockAsync(loc, this.Block);

        await Level.DestroyEntityAsync(this);
    }
}
