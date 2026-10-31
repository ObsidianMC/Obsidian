using Obsidian.Entities.AI;
using Obsidian.API.Inventory;
using Obsidian.Nbt;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:falling_block")]
public sealed partial class FallingBlock : Entity
{
    // Vanilla: each tick the downward speed drops by this much, then is scaled by the drag.
    private const double Gravity = 0.04;
    private const float AirDrag = 0.98f;

    public required IBlock Block { get; init; }

    public VectorD SpawnPosition { get; private set; }

    internal float StalactiteDamage { get; set; }

    internal bool DropItem { get; set; } = true;

    internal bool CancelDrop { get; set; }

    public FallingBlock(VectorD position) : base()
    {
        SpawnPosition = position;
        LastPosition = position;
        Position = position;
    }

    public async override ValueTask TickAsync()
    {
        LastPosition = Position;

        this.Motion -= new VectorD(0, Gravity, 0);
        var next = new VectorD(this.Position.X, this.Position.Y + this.Motion.Y, this.Position.Z);
        var grounded = false;
        if (this.Motion.Y < 0)
        {
            var halfWidth = this.Dimension.Width / 2.0;
            var swept = new BoundingBox(
                new VectorD(this.Position.X - halfWidth, next.Y, this.Position.Z - halfWidth),
                new VectorD(this.Position.X + halfWidth, this.Position.Y, this.Position.Z + halfWidth));
            var floor = next.Y;
            foreach (var shape in new MobTerrain(this.Level).GetCollisions(swept))
            {
                if (shape.Max.X <= swept.Min.X || shape.Min.X >= swept.Max.X
                    || shape.Max.Z <= swept.Min.Z || shape.Min.Z >= swept.Max.Z
                    || shape.Max.Y > this.Position.Y || shape.Max.Y < floor)
                {
                    continue;
                }

                floor = shape.Max.Y;
                grounded = true;
            }

            if (grounded)
            {
                next.Y = floor;
                this.Motion = new VectorD(this.Motion.X, 0, this.Motion.Z);
            }
        }

        await this.UpdateAsync(next, grounded ? MovementFlags.OnGround : MovementFlags.None);
        if (grounded)
        {
            // Vanilla resolves ground contact before its landing response, even while a piston defers placement.
            this.Motion = new VectorD(this.Motion.X * 0.7, this.Motion.Y * -0.5, this.Motion.Z * 0.7);
            await this.LandAsync((Vector)this.Position.Floor());
        }

        this.Motion *= AirDrag;
    }

    // Vanilla FallingBlockEntity.tick: a replaceable cell takes the block; any other cell discards it, dropping the
    // block's item where it landed. Replacing a block doesn't break it, so nothing drops for the cell it takes.
    private async Task LandAsync(Vector loc)
    {
        var target = await this.Level.GetBlockAsync(loc);
        if (target?.Is(BlockIds.MovingPiston) == true)
            return;

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

            var drops = this.DropItem && this.Level.LevelData.GetBooleanRule("entity_drops");
            if (!this.CancelDrop && drops && BlocksRegistry.TryGetItem(this.Block, out var spikeItem))
            {
                this.Level.SpawnItem(this.Position, new ItemStack(spikeItem));
            }

            // Vanilla's cancelled landing invokes the break hook regardless of item-drop settings.
            if ((this.CancelDrop || drops) && !this.Silent)
            {
                this.PacketBroadcaster.BroadcastToLevelInRange(this.Level, this.Position,
                    new Net.Packets.Play.Clientbound.LevelEventPacket(1052, loc, 0));
            }
            await this.Level.DestroyEntityAsync(this);
            return;
        }

        if (!this.CancelDrop && (target is null || target.CanBeReplaced()))
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
        else if (!this.CancelDrop && this.DropItem
            && this.Level.LevelData.GetBooleanRule("entity_drops")
            && BlocksRegistry.TryGetItem(this.Block, out var item))
        {
            Level.SpawnItem(new VectorD(Position.X, loc.Y, Position.Z), new ItemStack(item));
        }

        await Level.DestroyEntityAsync(this);
    }

    internal override void ReadNbt(NbtCompound tag)
    {
        base.ReadNbt(tag);
        this.DropItem = !tag.TryGetBool("DropItem", out var drops) || drops;
        this.CancelDrop = tag.TryGetBool("CancelDrop", out var cancel) && cancel;
    }

    internal override void WriteNbt(NbtCompound tag)
    {
        base.WriteNbt(tag);
        tag.Set(new NbtTag<bool>("DropItem", this.DropItem));
        tag.Set(new NbtTag<bool>("CancelDrop", this.CancelDrop));
    }

    internal static float StalactiteImpactDamage(double distance, float size)
    {
        // Vanilla's falling stalactite rounds distance up after its one-block grace, then caps damage at 40.
        var damagingDistance = (int)Math.Ceiling(distance - 1);
        return damagingDistance > 0 ? Math.Min(40, MathF.Floor(damagingDistance * size)) : 0;
    }
}
