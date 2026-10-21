using Obsidian.API.Inventory;
using Obsidian.Entities.AI;
using Obsidian.WorldData;
using Obsidian.WorldData.Fluids;

namespace Obsidian.Entities;

/// <summary>
/// The items that place boats and minecarts, like vanilla's <c>BoatItem</c> and <c>MinecartItem</c>, and dispensers
/// firing them, like vanilla's <c>BoatDispenseItemBehavior</c> and <c>MinecartDispenseItemBehavior</c>.
/// </summary>
internal static class VehicleItems
{
    private static readonly Dictionary<int, EntityType> minecarts = new()
    {
        [ItemIds.Minecart] = EntityType.Minecart,
        [ItemIds.ChestMinecart] = EntityType.ChestMinecart,
        [ItemIds.FurnaceMinecart] = EntityType.FurnaceMinecart,
        [ItemIds.TntMinecart] = EntityType.TntMinecart,
        [ItemIds.HopperMinecart] = EntityType.HopperMinecart,
        [ItemIds.CommandBlockMinecart] = EntityType.CommandBlockMinecart
    };

    /// <summary>The minecart an item places, or null for other items.</summary>
    public static EntityType? MinecartFor(Item item) => minecarts.TryGetValue(item.Id, out var type) ? type : null;

    /// <summary>
    /// Vanilla's <c>BoatItem.use</c>: the boat goes where the player looks, on the first block or liquid they look
    /// at within reach, facing their way, if it fits there and they aren't inside something they could hit instead.
    /// </summary>
    /// <returns>Whether the item was a boat, whose use is then done.</returns>
    public static async ValueTask<bool> UseBoatAsync(PlayerBase player, InteractionHand hand, ItemStack item)
    {
        if (Boat.TypeFor(item.Holder) is not EntityType type)
            return false;

        if (player.GameMode == GameMode.Spectator || !player.Alive || player.Level is not AbstractLevel level)
            return true;

        var eye = PlayerBase.EyePosition(player);
        var look = (VectorD)player.GetLookDirection();
        var reach = PlayerBase.BlockInteractionRange(player);
        if (Clip(level, eye, eye + look * reach) is not VectorD hit)
            return true;

        // Vanilla doesn't place a boat while the player's eyes are inside something they could hit.
        if (level.GetEntitiesInRange(player.Position, 8)
            .OfType<Entity>()
            .Any(entity => !ReferenceEquals(entity, player)
                && IsPickable(entity)
                && Contains(entity.Dimension.CreateBBFromPosition(entity.Position), eye)))
        {
            return true;
        }

        var boat = Boat.Create(type, level);
        boat.EntityId = Server.GetNextEntityId();
        boat.Position = hit;
        boat.LastPosition = hit;
        boat.Yaw = player.Yaw;
        boat.CustomName = item.CustomName;

        var bounds = boat.Dimension.CreateBBFromPosition(hit);
        if (!new MobTerrain(level).IsFree(bounds) || OverlapsSolidEntity(level, bounds))
            return true;

        if (!level.TrySpawnEntity(boat, hostOwned: true))
            return true;

        if (player.GameMode != GameMode.Creative)
        {
            var slot = hand == InteractionHand.OffHand ? 45 : player.CurrentHeldItemSlot;
            player.Inventory.RemoveItem(slot, 1);
            await player.SyncInventorySlotAsync(slot);
        }

        player.Statistics.Increment(item.Holder.UnlocalizedName, type: "minecraft:used");
        return true;
    }

    /// <summary>
    /// Vanilla's <c>MinecartItem.useOn</c>: a minecart goes on the rail clicked, at its middle, half a block higher on
    /// a slope.
    /// </summary>
    /// <returns>The minecart placed, or null when the block isn't a rail.</returns>
    public static AbstractMinecart? PlaceMinecart(AbstractLevel level, EntityType type, Vector rail, IBlock railState, ItemStack item)
    {
        if (!Rails.IsRail(railState))
            return null;

        var raise = Rails.IsSlope(Rails.ShapeOf(railState)) ? 0.5 : 0;
        var position = new VectorD(rail.X + 0.5, rail.Y + 0.0625 + raise, rail.Z + 0.5);
        return SpawnMinecart(level, type, position, item);
    }

    // Vanilla's AbstractMinecart.createMinecart.
    private static AbstractMinecart? SpawnMinecart(AbstractLevel level, EntityType type, VectorD position, ItemStack item)
    {
        if (Factories.EntitySpawner.Create(type, level) is not AbstractMinecart minecart)
            return null;

        minecart.Type = type;
        minecart.EntityId = Server.GetNextEntityId();
        minecart.Position = position;
        minecart.LastPosition = position;
        minecart.CustomName = item.CustomName;

        return level.TrySpawnEntity(minecart, hostOwned: true) ? minecart : null;
    }

    /// <summary>
    /// A dispenser firing a boat or a minecart (vanilla's <c>BoatDispenseItemBehavior</c> and
    /// <c>MinecartDispenseItemBehavior</c>): a boat goes into the water or onto the air before it, a minecart onto the
    /// rail before it or one below that. Otherwise the item drops like any other.
    /// </summary>
    /// <returns>Whether the item placed a vehicle; false when it should be dropped instead.</returns>
    public static bool Dispense(AbstractLevel level, Vector dispenser, BlockFace facing, ItemStack item)
    {
        if (Boat.TypeFor(item.Holder) is EntityType boatType)
            return DispenseBoat(level, dispenser, facing, boatType, item);

        if (MinecartFor(item.Holder) is EntityType cartType)
            return DispenseMinecart(level, dispenser, facing, cartType, item);

        return false;
    }

    private static bool DispenseBoat(AbstractLevel level, Vector dispenser, BlockFace facing, EntityType type, ItemStack item)
    {
        var step = facing.ToVector();
        var terrain = new MobTerrain(level);
        var width = 1.375f;
        var reach = 0.5625 + width / 2;
        var x = dispenser.X + 0.5 + step.X * reach;
        var y = dispenser.Y + 0.5 + step.Y * 1.125;
        var z = dispenser.Z + 0.5 + step.Z * reach;

        var front = dispenser + step;
        var below = front + Vector.Down;
        double raise;
        if (terrain.GetBlock(front) is IBlock frontBlock && FluidState.Of(frontBlock).IsWater)
            raise = 1;
        else if (terrain.GetBlock(front)?.IsAir == true
            && terrain.GetBlock(below) is IBlock belowBlock
            && FluidState.Of(belowBlock).IsWater)
            raise = 0;
        else
            return false;

        var boat = Boat.Create(type, level);
        boat.EntityId = Server.GetNextEntityId();
        boat.Position = new VectorD(x, y + raise, z);
        boat.LastPosition = boat.Position;
        boat.Yaw = YawOf(facing);
        boat.CustomName = item.CustomName;
        return level.TrySpawnEntity(boat, hostOwned: true);
    }

    private static bool DispenseMinecart(AbstractLevel level, Vector dispenser, BlockFace facing, EntityType type, ItemStack item)
    {
        var step = facing.ToVector();
        var terrain = new MobTerrain(level);
        var front = dispenser + step;
        var x = dispenser.X + 0.5 + step.X * 1.125;
        var y = (double)dispenser.Y + step.Y;
        var z = dispenser.Z + 0.5 + step.Z * 1.125;

        double raise;
        if (terrain.GetBlock(front) is IBlock rail && Rails.IsRail(rail))
        {
            raise = Rails.IsSlope(Rails.ShapeOf(rail)) ? 0.6 : 0.1;
        }
        else
        {
            if (terrain.GetBlock(front)?.IsAir != true
                || terrain.GetBlock(front + Vector.Down) is not IBlock under
                || !Rails.IsRail(under))
            {
                return false;
            }

            raise = facing != BlockFace.Down && Rails.IsSlope(Rails.ShapeOf(under)) ? -0.4 : -0.9;
        }

        return SpawnMinecart(level, type, new VectorD(x, y + raise, z), item) is not null;
    }

    // A dispenser's facing as a yaw, like vanilla's Direction.toYRot.
    private static float YawOf(BlockFace facing) => facing switch
    {
        BlockFace.South => 0,
        BlockFace.West => 90,
        BlockFace.North => 180,
        BlockFace.East => 270,
        _ => 0
    };

    // Vanilla's getPlayerPOVHitResult with ClipContext.Fluid.ANY: the nearest block outline or liquid surface the
    // segment meets.
    private static VectorD? Clip(AbstractLevel level, VectorD start, VectorD end)
    {
        var terrain = new MobTerrain(level);
        var direction = end - start;
        double? nearest = null;

        if (terrain.RaycastOutlines(start, end) is API.Blocks.BlockShapeHit block)
            nearest = ((VectorD)block.Position + block.ClickedAt - start).Magnitude / direction.Magnitude;

        foreach (var cell in MobTerrain.CellsAlong(start, end))
        {
            if (terrain.GetBlock(cell) is not IBlock state)
                break;

            var fluid = FluidState.Of(state);
            if (fluid.IsEmpty)
                continue;

            var above = terrain.GetBlock(cell + Vector.Up);
            var height = above is not null && FluidState.Of(above).Kind is var kind
                && (fluid.IsWater ? FluidState.IsWaterKind(kind) : FluidState.Of(above).IsLava)
                ? 1
                : fluid.OwnHeight;
            var surface = new BoundingBox((VectorD)cell, (VectorD)cell + new VectorD(1, height, 1));
            if (MobTerrain.RayIntersection(surface, start, direction) is double fraction)
            {
                if (nearest is null || fraction < nearest)
                    nearest = fraction;

                break;
            }
        }

        return nearest is double hit ? start + direction * hit : null;
    }

    // Vanilla's EntitySelector.CAN_BE_PICKED: what a player's look can hit, other than spectators.
    private static bool IsPickable(Entity entity) => entity switch
    {
        IPlayer { GameMode: GameMode.Spectator } => false,
        VehicleEntity vehicle => !vehicle.IsRemoved,
        Living living => living.Alive,
        HangingEntity or EndCrystal => true,
        _ => false
    };

    // Vanilla's Level.noCollision also counts the entities others collide with: boats and minecarts.
    private static bool OverlapsSolidEntity(AbstractLevel level, BoundingBox bounds) => level
        .GetEntitiesInRange(bounds.Min + (bounds.Max - bounds.Min) / 2, 4)
        .OfType<VehicleEntity>()
        .Any(vehicle => !vehicle.IsRemoved
            && MobTerrain.Overlaps(bounds, vehicle.Dimension.CreateBBFromPosition(vehicle.Position)));

    private static bool Contains(BoundingBox box, VectorD point) =>
        point.X >= box.Min.X && point.X < box.Max.X
        && point.Y >= box.Min.Y && point.Y < box.Max.Y
        && point.Z >= box.Min.Z && point.Z < box.Max.Z;
}
