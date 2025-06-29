namespace Obsidian.Entities;

[MinecraftEntity("minecraft:falling_block")]
public sealed partial class FallingBlock : Entity
{
    public required IBlock Block { get; init; }

    public VectorF SpawnPosition { get; private set; }

    private int AliveTime { get; set; }

    private VectorF DeltaPosition { get; set; }

    private readonly float gravity = 1.75F;

    private readonly float windResFactor = 0.98F;

    private HashSet<Vector> checkedBlocks = new();

    public FallingBlock(VectorF position) : base()
    {
        SpawnPosition = position;
        LastPosition = position;
        Position = position;
        AliveTime = 0;
        DeltaPosition = VectorF.Zero;
    }

    public async override ValueTask TickAsync()
    {
        AliveTime++;
        LastPosition = Position;
        var deltaY = (Math.Pow(windResFactor, AliveTime) - 1) * gravity;
        DeltaPosition = new VectorF(0.0F, (float)deltaY, 0.0F);
        Position += DeltaPosition;

        // Check below to see if we're about to hit a solid block.
        var upcomingBlockPos = new Vector(
            (int)Math.Floor(Position.X),
            (int)Math.Floor(Position.Y - 1),
            (int)Math.Floor(Position.Z));

        if (!checkedBlocks.Contains(upcomingBlockPos))
        {
            checkedBlocks.Add(upcomingBlockPos);

            var upcomingBlock = await World.GetBlockAsync(upcomingBlockPos);
            if (upcomingBlock is IBlock && !TagsRegistry.Block.ReplaceableByLiquid.Entries.Contains(upcomingBlock.RegistryId) && !upcomingBlock.IsLiquid)
            {
                await ConvertToBlock(upcomingBlockPos + Vector.Up);
            }
        }
    }

    private async Task ConvertToBlock(Vector loc)
    {
        await World.SetBlockAsync(loc, this.Block);

        await World.DestroyEntityAsync(this);
    }
}
