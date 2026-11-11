namespace Obsidian.Entities.AI;

/// <summary>
/// How the blocks an entity is in and stands on change how it moves, for entities the server moves (mobs, items):
/// soul sand and honey slow anything moving over them, and honey halves a jump.
/// </summary>
/// <remarks>
/// Like vanilla's <c>getBlockSpeedFactor</c> and <c>getBlockJumpFactor</c>, the block the entity is in decides first,
/// and only when it doesn't change movement does the block below it (vanilla's
/// <c>getBlockPosBelowThatAffectsMyMovement</c>, half a block under the feet). Players are moved by their clients, which
/// apply these themselves.
/// </remarks>
internal static class BlockMovementFactors
{
    // Vanilla's getOnPos(0.500001F): how far under the feet the block that affects movement is.
    private const double BelowDepth = 0.500001f;

    private const string MovementEfficiency = "minecraft:movement_efficiency";

    /// <summary>
    /// What the entity's horizontal velocity is multiplied by once it has moved to <paramref name="position"/>, as
    /// vanilla's <c>Entity.move</c> ends: the block's speed factor, which a living entity's movement efficiency
    /// attribute brings toward 1.
    /// </summary>
    internal static float Speed(Entity entity, MobTerrain terrain, VectorD position, bool onGround)
    {
        var inBlock = terrain.GetBlock((Vector)position.Floor());
        var factor = SpeedFactorOf(inBlock);

        // Water and bubble columns decide on their own, even though they don't slow anything.
        var fluid = inBlock?.RegistryId is BlockIds.Water or BlockIds.BubbleColumn;
        if (factor == 1 && !fluid)
            factor = SpeedFactorOf(BlockBelow(entity, terrain, position, onGround));

        if (factor == 1 || entity is not Living living || !living.HasAttribute(MovementEfficiency))
            return factor;

        // LivingEntity's Mth.lerp(efficiency, factor, 1), in float as vanilla rounds it.
        var efficiency = living.GetAttributeValue(MovementEfficiency);
        return factor + efficiency * (1 - factor);
    }

    /// <summary>What a jump's strength is multiplied by where the entity stands now.</summary>
    internal static float Jump(Entity entity, MobTerrain terrain)
    {
        var position = entity.Position;
        var inBlock = JumpFactorOf(terrain.GetBlock((Vector)position.Floor()));
        if (inBlock != 1)
            return inBlock;

        var onGround = entity.MovementFlags.HasFlag(MovementFlags.OnGround);
        return JumpFactorOf(BlockBelow(entity, terrain, position, onGround));
    }

    // The block properties' speedFactor and jumpFactor in vanilla 26.3's Blocks; every other block's are 1.
    private static float SpeedFactorOf(IBlock? block) =>
        block?.RegistryId is BlockIds.SoulSand or BlockIds.HoneyBlock ? 0.4f : 1;

    private static float JumpFactorOf(IBlock? block) => block?.RegistryId == BlockIds.HoneyBlock ? 0.5f : 1;

    private static bool ChangesMovement(IBlock? block) =>
        block?.RegistryId is BlockIds.SoulSand or BlockIds.HoneyBlock;

    /// <summary>
    /// The block below <paramref name="entity"/> that affects its movement, or null when none of the blocks half a block
    /// under its feet changes movement, which is nearly always, and then the block holding it up isn't looked for.
    /// </summary>
    private static IBlock? BlockBelow(Entity entity, MobTerrain terrain, VectorD position, bool onGround)
    {
        var y = (int)Math.Floor(position.Y - BelowDepth);
        var box = entity.Dimension.CreateBBFromPosition(position);
        if (!AnyChangesMovement(terrain, box, y))
            return null;

        // On the ground, the block holding the entity up decides the column, as vanilla's mainSupportingBlockPos does.
        var cell = onGround && entity.Level is WorldData.IContactLevel level
            ? WorldData.EntityContactPass.MovementCellAt(level, entity, position, y)
            : new Vector((int)Math.Floor(position.X), y, (int)Math.Floor(position.Z));
        return terrain.GetBlock(cell);
    }

    private static bool AnyChangesMovement(MobTerrain terrain, BoundingBox box, int y)
    {
        var minX = (int)Math.Floor(box.Min.X);
        var maxX = (int)Math.Floor(box.Max.X);
        var minZ = (int)Math.Floor(box.Min.Z);
        var maxZ = (int)Math.Floor(box.Max.Z);
        for (var z = minZ; z <= maxZ; z++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                if (ChangesMovement(terrain.GetBlock(new Vector(x, y, z))))
                    return true;
            }
        }

        return false;
    }
}
