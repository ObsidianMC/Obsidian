using Obsidian.Utilities.Registry;
using Obsidian.WorldData;

namespace Obsidian.Entities;

public class FallingBlock : Entity
{
    public VectorF SpawnPosition { get; private set; }

    public Material BlockMaterial { get; set; }

    private int AliveTime { get; set; }

    private VectorF DeltaPosition { get; set; }

    private readonly VectorF gravity = new VectorF(0F, -0.02F, 0F);

    private readonly float windResFactor = 0.98F;

    private HashSet<Vector> checkedBlocks = new();

    private World world;

    public FallingBlock(World world) : base()
    {
        SpawnPosition = Position;
        LastPosition = Position;
        AliveTime = 0;
        DeltaPosition = new VectorF(0F, 0F, 0F);
        this.world = world;
    }

    public async override Task TickAsync()
    {
        AliveTime++;
        LastPosition = Position;
        if (!this.NoGravity)
            DeltaPosition += gravity; // y - 0.04
        DeltaPosition *= windResFactor; // * 0.98
        Position += DeltaPosition;

        // Check below to see if we're about to hit a solid block.
        var upcomingBlockPos = new Vector(
            (int)Math.Floor(Position.X),
            (int)Math.Floor(Position.Y - 1),
            (int)Math.Floor(Position.Z));

        if (!checkedBlocks.Contains(upcomingBlockPos))
        {
            checkedBlocks.Add(upcomingBlockPos);

            var upcomingBlock = await world.GetBlockAsync(upcomingBlockPos);

            if (upcomingBlock is IBlock block &&
                (!block.IsLiquid || !block.IsAir)  &&
                block.Material != Material.Grass &&
                block.Material != Material.DeadBush &&
                block.Material != Material.Snow
                )
            {
                await ConvertToBlock(upcomingBlockPos + (0, 1, 0));
            }
        }
    }

    private async Task ConvertToBlock(Vector loc)
    {
        var block = BlocksRegistry.Get(BlockMaterial);
        await world.SetBlockUntrackedAsync(loc, block);

        await world.SetBlockAsync(loc, block);

        await world.DestroyEntityAsync(this);
    }
}
