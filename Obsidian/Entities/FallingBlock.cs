using Microsoft.CodeAnalysis;
using Obsidian.Registries;
using Obsidian.WorldData;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:falling_block")]
public sealed partial class FallingBlock : Entity
{
    public required IBlock Block { get; init; } 

    public VectorF SpawnPosition { get; private set; }

    private int AliveTime { get; set; }

    private VectorF DeltaPosition { get; set; }

    private readonly VectorF gravity = new VectorF(0F, -0.02F, 0F);

    private readonly float windResFactor = 0.98F;

    private HashSet<Vector> checkedBlocks = new();

    private World world;

    public FallingBlock(World world, VectorF position) : base()
    {
        SpawnPosition = position;
        LastPosition = position;
        Position = position;
        AliveTime = 0;
        DeltaPosition = new VectorF(0F, 0F, 0F);
        this.world = world;
    }

    public async override Task TickAsync()
    {
        if (AliveTime == 0)
        {
            //world.AcknowledgeBlockChange();
            //await world.SetBlockAsync((Vector)SpawnPosition, BlocksRegistry.Air);
        }
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
            if (upcomingBlock is IBlock block && !TagsRegistry.Blocks.ReplaceableByLiquid.Entries.Contains(block.RegistryId) && !block.IsLiquid)
            {
                await ConvertToBlock(upcomingBlockPos + (0, 1, 0));
            }
        }
    }

    private async Task ConvertToBlock(Vector loc)
    {
        await world.SetBlockAsync(loc, this.Block);

        await world.DestroyEntityAsync(this);
    }
}
