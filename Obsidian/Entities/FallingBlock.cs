using Obsidian.Entities.AI;
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

    internal float StalactiteDamage { get; set; }

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

            // Only a support whose top the feet have passed stops the fall; a partial-height one below may not yet.
            var boxes = BlockCollisionShapes.Get(support);
            if (boxes.Count == 0 || next.Y > cell + boxes.Max(box => box.Max.Y))
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
        if (this.Block.Is(BlockIds.SulfurSpike))
        {
            if (this.StalactiteDamage > 0)
            {
                var damage = StalactiteImpactDamage(this.SpawnPosition.Y - this.Position.Y, this.StalactiteDamage);
                if (damage > 0)
                {
                    foreach (var victim in this.Level.GetEntitiesInRange(this.Position, 3).OfType<Living>())
                    {
                        if (MobTerrain.Overlaps(this.BoundingBox, victim.BoundingBox))
                        {
                            await victim.DamageCombatAsync(this, damage, CombatDamageKind.FallingStalactite);
                        }
                    }
                }
            }

            if (BlocksRegistry.TryGetItem(this.Block, out var spikeItem))
            {
                this.Level.SpawnItem(this.Position, new ItemStack(spikeItem));
            }

            this.PacketBroadcaster.BroadcastToLevelInRange(this.Level, this.Position,
                new Net.Packets.Play.Clientbound.LevelEventPacket(1052, loc, 0));
            await this.Level.DestroyEntityAsync(this);
            return;
        }

        var target = await Level.GetBlockAsync(loc);
        if (target is null || target.CanBeReplaced())
        {
            // Like vanilla's, with updates: the landed block checks below it again, and its neighbours hear of it.
            await Level.SetBlockAsync(loc, this.Block, doBlockUpdate: true);
            // 26.3 keeps a transient copy visible as the falling entity is removed after landing.
            var landed = await Level.GetBlockAsync(loc);
            if (landed is not null)
            {
                PacketBroadcaster.BroadcastToLevelInRange(Level, Position,
                    new Net.Packets.Play.Clientbound.AddTransientBlockPacket(loc, landed.State?.Id ?? landed.DefaultId));
            }
        }
        else if (BlocksRegistry.TryGetItem(this.Block, out var item))
        {
            Level.SpawnItem(new VectorD(Position.X, loc.Y, Position.Z), new ItemStack(item));
        }

        await Level.DestroyEntityAsync(this);
    }

    internal static float StalactiteImpactDamage(double distance, float size)
    {
        // Vanilla's falling stalactite rounds distance up after its one-block grace, then caps damage at 40.
        var damagingDistance = (int)Math.Ceiling(distance - 1);
        return damagingDistance > 0 ? Math.Min(40, MathF.Floor(damagingDistance * size)) : 0;
    }
}
