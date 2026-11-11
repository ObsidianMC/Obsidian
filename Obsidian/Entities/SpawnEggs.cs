using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Entities.AI;
using Obsidian.WorldData;

namespace Obsidian.Entities;

/// <summary>What vanilla's spawn eggs (<c>SpawnEggItem</c>) spawn, and where.</summary>
internal static class SpawnEggs
{
    /// <summary>
    /// The entity data a spawn egg spawns from: the stack's own, or the one its item gives every stack.
    /// </summary>
    internal static EntityDataComponent? EntityData(ItemStack stack) =>
        stack.GetComponent<EntityDataComponent>(DataComponentType.EntityData) ?? DefaultEntity(stack);

    /// <summary>
    /// The vanilla entity a spawn egg spawns (vanilla's <c>SpawnEggItem.getType</c>), or null when the stack isn't a
    /// spawn egg of one.
    /// </summary>
    internal static EntityType? TypeOf(ItemStack stack)
    {
        if (!stack.Holder.UnlocalizedName.EndsWith("_spawn_egg", StringComparison.Ordinal))
            return null;

        return EntityData(stack) is { } data && Enum.IsDefined((EntityType)data.RegistryId)
            ? (EntityType)data.RegistryId
            : null;
    }

    /// <summary>
    /// Vanilla's <c>EntityType.getYOffset</c>: how far above <paramref name="target"/>'s floor the mob stands when it
    /// starts a block higher and settles onto the blocks in <paramref name="target"/>.
    /// </summary>
    internal static double SettledOffset(AbstractLevel level, Mob mob, Vector target)
    {
        var raised = mob.Dimension.CreateBBFromPosition((VectorD)target + new VectorD(0.5, 1, 0.5));
        var search = new BoundingBox((VectorD)target, (VectorD)target + new VectorD(1, 1, 1));
        var collisions = new MobTerrain(level).GetCollisions(search)
            .Where(shape => shape.Max.X > search.Min.X && shape.Min.X < search.Max.X
                && shape.Max.Y > search.Min.Y && shape.Min.Y < search.Max.Y
                && shape.Max.Z > search.Min.Z && shape.Min.Z < search.Max.Z)
            .ToArray();

        return 1 + EntityMovement.Clip(raised, new VectorD(0, -1, 0), collisions).Y;
    }

    // The entity a spawn egg's own entity_data names, for a stack that doesn't carry the component itself.
    private static EntityDataComponent? DefaultEntity(ItemStack stack)
    {
        var data = CombatItems.Component(stack, "entity_data");
        if (data.ValueKind != System.Text.Json.JsonValueKind.Object || !data.TryGetProperty("id", out var id))
            return null;

        if (!EntityNbt.TryParseType(id.GetString(), out var type))
            return null;

        return new EntityDataComponent(DataComponentType.EntityData) { RegistryId = (int)type };
    }
}
