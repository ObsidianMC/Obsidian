using Obsidian.API.Inventory;

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
        var fromCell = (int)Math.Floor(Position.Y);
        var toCell = (int)Math.Floor(next.Y);

        // Sends the move to clients, so they follow the fall instead of seeing it jump to the landing spot.
        await UpdateAsync(next, MovementFlags.None);

        // Vanilla's collision stops the block on the first block with a collision shape whose top the feet pass.
        // Blocks without one (torches, flowers) don't stop it. The block lands in the cell the feet were in.
        for (var cell = fromCell - 1; cell >= toCell; cell--)
        {
            var support = await Level.GetBlockAsync(new Vector((int)Math.Floor(next.X), cell, (int)Math.Floor(next.Z)));
            if (support is null || support.HasEmptyCollision())
                continue;

            await LandAsync(new Vector((int)Math.Floor(next.X), cell + 1, (int)Math.Floor(next.Z)));
            return;
        }

        VelocityY *= AirDrag;
    }

    // Vanilla FallingBlockEntity.tick: a replaceable cell takes the block; any other cell discards it, dropping the
    // block's item where it landed. Replacing a block doesn't break it, so nothing drops for the cell it takes.
    private async Task LandAsync(Vector loc)
    {
        var target = await Level.GetBlockAsync(loc);
        if (target is null || target.CanBeReplaced())
        {
            // Like vanilla's, with updates: the landed block checks below it again, and its neighbours hear of it.
            await Level.SetBlockAsync(loc, this.Block, doBlockUpdate: true);
        }
        else if (BlocksRegistry.TryGetItem(this.Block, out var item))
        {
            Level.SpawnItem(new VectorD(Position.X, loc.Y, Position.Z), new ItemStack(item));
        }

        await Level.DestroyEntityAsync(this);
    }
}
