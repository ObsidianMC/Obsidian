using Obsidian.API.Inventory;

namespace Obsidian.Entities.AI;

/// <summary>
/// The collision of the blocks whose shape depends on the entity moving into them, as vanilla's
/// <c>EntityCollisionContext</c> gives it: powder snow, which holds up only what can walk on it, and scaffolding, which
/// holds up what stands on it unless it's sneaking.
/// </summary>
/// <remarks>
/// Every other block collides the same for every entity, and what has no entity (placement, pathfinding, block
/// support) keeps <see cref="BlockCollisionShapes.Get(IBlock)"/>. The shapes are vanilla's constants, in block-local
/// coordinates, and shared: they're never changed.
/// </remarks>
internal static class EntityCollisionShapes
{
    // Vanilla's EntityCollisionContext.isAbove: the feet are above the shape's top, less 1.0E-5F.
    private const double AboveMargin = 1.0E-5f;

    // PowderSnowBlock's FALLING_COLLISION_SHAPE, for an entity that has fallen more than this far.
    private const double PowderSnowFallDistance = 2.5;

    private static readonly BoundingBox[] none = [];

    private static readonly BoundingBox[] fullBlock = [Box(0, 0, 0, 1, 1, 1)];

    private static readonly BoundingBox[] powderSnowFalling = [Box(0, 0, 0, 1, 0.9f, 1)];

    // ScaffoldingBlock's SHAPE_STABLE (a top two pixels thick on four corner legs) and SHAPE_UNSTABLE_BOTTOM.
    private static readonly BoundingBox[] scaffoldingStable =
    [
        Box(0, 0.875, 0, 1, 1, 1),
        Box(0, 0, 0, 0.125, 1, 0.125),
        Box(0.875, 0, 0, 1, 1, 0.125),
        Box(0, 0, 0.875, 0.125, 1, 1),
        Box(0.875, 0, 0.875, 1, 1, 1),
    ];

    private static readonly BoundingBox[] scaffoldingBottom = [Box(0, 0, 0, 1, 0.125, 1)];

    /// <summary>Whether <paramref name="block"/>'s collision depends on the entity moving into it.</summary>
    public static bool DependsOnEntity(IBlock block) => block.RegistryId is BlockIds.PowderSnow or BlockIds.Scaffolding;

    /// <summary>
    /// The collision of <paramref name="block"/> at <paramref name="position"/> for <paramref name="entity"/> with its
    /// feet at <paramref name="feetY"/>, for a block <see cref="DependsOnEntity"/> says depends on it.
    /// </summary>
    /// <remarks>
    /// Vanilla reads the feet where the entity is when it looks: before a move for the move's collision, and after it
    /// for the block that ends up holding it up.
    /// </remarks>
    public static IReadOnlyList<BoundingBox> Get(Vector position, IBlock block, Entity entity, double feetY) =>
        block.RegistryId == BlockIds.PowderSnow
            ? PowderSnow(position, entity, feetY)
            : Scaffolding(position, block, entity, feetY);

    /// <summary>
    /// Whether <paramref name="entity"/> can walk on powder snow, like vanilla's <c>canEntityWalkOnPowderSnow</c>:
    /// rabbits, endermites, silverfish and foxes, and anything living with leather boots on its feet.
    /// </summary>
    public static bool CanWalkOnPowderSnow(Entity entity)
    {
        if (entity.Type is EntityType type && TagsRegistry.EntityType.PowderSnowWalkableMobs.Entries.Contains((int)type))
            return true;

        var feet = entity switch
        {
            PlayerBase player => player.Inventory.GetItem(8),
            Mob mob => mob.GetEquipment(EquipmentSlot.Boots),
            _ => null,
        };
        return feet is { Count: > 0 } && feet.Is(ItemsRegistry.LeatherBoots);
    }

    // PowderSnowBlock.getCollisionShape: a falling entity is caught short of the top, a falling block and what walks on
    // it standing above it (and not sneaking) are held up, and everything else sinks in.
    private static BoundingBox[] PowderSnow(Vector position, Entity entity, double feetY)
    {
        if (entity.ContactFallDistance > PowderSnowFallDistance)
            return powderSnowFalling;

        if (entity is FallingBlock)
            return fullBlock;

        var standsOn = CanWalkOnPowderSnow(entity) && IsAbove(feetY, position, 1) && !entity.Sneaking;
        return standsOn ? fullBlock : none;
    }

    // ScaffoldingBlock.getCollisionShape: whole from above unless sneaking; otherwise only the bottom of an unsupported
    // piece (distance above 0, bottom), and only for what isn't below it.
    private static BoundingBox[] Scaffolding(Vector position, IBlock block, Entity entity, double feetY)
    {
        if (IsAbove(feetY, position, 1) && !entity.Sneaking)
            return scaffoldingStable;

        var unsupported = block.GetProperty("distance") != "0" && block.GetProperty("bottom") == "true";
        return unsupported && IsAbove(feetY, position, 0) ? scaffoldingBottom : none;
    }

    private static bool IsAbove(double feetY, Vector position, double top) =>
        feetY > position.Y + top - AboveMargin;

    private static BoundingBox Box(double minX, double minY, double minZ, double maxX, double maxY, double maxZ) =>
        new(new VectorD(minX, minY, minZ), new VectorD(maxX, maxY, maxZ));
}
