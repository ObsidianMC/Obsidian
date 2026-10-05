namespace Obsidian.Entities;

[MinecraftEntity("minecraft:falling_block")]
public sealed partial class FallingBlock : Entity
{
    public required IBlock Block { get; init; }

    public VectorD SpawnPosition { get; private set; }

    private int AliveTime { get; set; }

    private VectorD DeltaPosition { get; set; }

    private readonly float gravity = 1.75F;

    private readonly float windResFactor = 0.98F;

    private readonly HashSet<Vector> checkedBlocks = [];

    public FallingBlock(VectorD position) : base()
    {
        SpawnPosition = position;
        LastPosition = position;
        Position = position;
        AliveTime = 0;
        DeltaPosition = VectorD.Zero;
    }

    public async override ValueTask TickAsync()
    {
        AliveTime++;
        LastPosition = Position;
        var deltaY = (Math.Pow(windResFactor, AliveTime) - 1) * gravity;
        DeltaPosition = new VectorD(0, deltaY, 0);
        Position += DeltaPosition;

        // Check below to see if we're about to hit a solid block.
        var upcomingBlockPos = new Vector(
            (int)Math.Floor(Position.X),
            (int)Math.Floor(Position.Y - 1),
            (int)Math.Floor(Position.Z));

        if (checkedBlocks.Add(upcomingBlockPos))
        {
            var upcomingBlock = await Level.GetBlockAsync(upcomingBlockPos);
            if (upcomingBlock is not null && !upcomingBlock.IsFreeForFallingBlock())
            {
                await ConvertToBlock(upcomingBlockPos + Vector.Up);
            }
        }
    }

    private async Task ConvertToBlock(Vector loc)
    {
        await Level.SetBlockAsync(loc, this.Block);

        await Level.DestroyEntityAsync(this);
    }
}
