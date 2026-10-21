using Obsidian.API.BlockEntities;
using Obsidian.API.Blocks;
using Obsidian.API.Containers;
using Obsidian.API.Inventory;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// The lectern's block, like vanilla's <c>LecternBlock</c>: turning a page powers it for two game ticks, weakly to
/// its neighbours and strongly to the block under it.
/// </summary>
internal sealed class LecternComponent : RedstoneComponent
{
    public override bool IsSignalSource(LevelBlockUpdates level) => true;

    public override int GetSignal(LevelBlockUpdates level, Vector position, IBlock state, BlockFace direction) =>
        state.GetProperty("powered") == "true" ? 15 : 0;

    public override int GetDirectSignal(LevelBlockUpdates level, Vector position, IBlock state, BlockFace direction) =>
        direction == BlockFace.Up ? this.GetSignal(level, position, state, direction) : 0;

    // The page turn's pulse ends.
    public override void Tick(LevelBlockUpdates level, Vector position, IBlock state) =>
        SetPowered(level, position, state, false);

    /// <summary>
    /// Vanilla's <c>changePowered</c>: the lectern and the block under it hear of the change.
    /// </summary>
    public static void SetPowered(LevelBlockUpdates level, Vector position, IBlock state, bool powered)
    {
        if (state.RegistryId != BlockIds.Lectern || (state.GetProperty("powered") == "true") == powered)
            return;

        var changed = state.WithProperty("powered", powered);
        level.Set(position, changed, BlockUpdateFlags.All);
        level.UpdateNeighbors(position + new Vector(0, -1, 0), changed);
    }
}

/// <summary>
/// The crafter's block, like vanilla's <c>CrafterBlock</c>: it faces away from the player, and a rising redstone
/// signal crafts what its grid holds four game ticks later, putting the result in the container it faces or dropping
/// it, with the ingredients' remainders.
/// </summary>
internal sealed class CrafterComponent : RedstoneComponent
{
    private const int CraftDelay = 4;

    // How long the crafter shows itself crafting (vanilla's CrafterBlockEntity crafting ticks).
    private const int CraftingTicks = 6;

    // Vanilla's LevelEvent.SOUND_CRAFTER_CRAFT, SOUND_CRAFTER_FAIL and PARTICLES_CRAFTER_CRAFT.
    private const int CraftEvent = 1049;
    private const int FailEvent = 1050;
    private const int ParticlesEvent = 2010;

    // Vanilla's getStateForPlacement: it faces away from the player; its top faces up, or, facing up or down, the
    // way the player looks.
    public override IBlock? GetStateForPlacement(RedstonePlacement placement)
    {
        var front = placement.NearestLookDirection.Opposite();
        var top = front switch
        {
            BlockFace.Down => placement.HorizontalDirection.Opposite(),
            BlockFace.Up => placement.HorizontalDirection,
            _ => BlockFace.Up
        };

        return placement.Block
            .WithProperty("orientation", $"{FeatureHelpers.FaceName(front)}_{FeatureHelpers.FaceName(top)}")
            .WithProperty("triggered", placement.Level.HasNeighborSignal(placement.Position));
    }

    public override void NeighborChanged(LevelBlockUpdates level, Vector position, IBlock state)
    {
        var powered = level.HasNeighborSignal(position);
        var triggered = state.GetProperty("triggered") == "true";

        if (powered && !triggered)
        {
            level.ScheduleTick(position, state, CraftDelay);
            level.Set(position, state.WithProperty("triggered", true), BlockUpdateFlags.Clients);
            SetTriggered(level, position, true);
        }
        else if (!powered && triggered)
        {
            level.Set(position, state.WithProperty("triggered", false), BlockUpdateFlags.Clients);
            SetTriggered(level, position, false);
        }
    }

    /// <remarks>
    /// Crafts with the level's entity actions, like the dispenser fires: crafting takes the crafter's container lock.
    /// </remarks>
    public override void Tick(LevelBlockUpdates level, Vector position, IBlock state)
    {
        if (level.Level is AbstractLevel owner)
            owner.EnqueueEntityAction(() => Craft(owner, position));
    }

    private static void SetTriggered(LevelBlockUpdates level, Vector position, bool triggered)
    {
        if (level.GetBlockEntity(position) is CrafterBlockEntity crafter)
        {
            crafter.Crafter.Triggered = triggered;
            crafter.SetChanged();
        }
    }

    // Vanilla's dispenseFrom: the grid's recipe, its disabled slots counting as empty, crafts once; the result and the
    // ingredients' remainders go out the front, and each ingredient is used up by one.
    private static async ValueTask Craft(AbstractLevel level, Vector position)
    {
        var updates = level.BlockUpdates;
        var state = updates.GetBlock(position);
        if (state.RegistryId != BlockIds.Crafter || updates.GetBlockEntity(position) is not CrafterBlockEntity entity)
            return;

        var crafter = entity.Crafter;
        ItemStack[] outputs;

        using (await BaseContainer.LockAllAsync([crafter]))
        {
            var grid = Enumerable.Range(0, CrafterContainer.GridSize)
                .Select(slot => crafter.IsSlotDisabled(slot) ? null : crafter.GetItem(slot))
                .ToArray();

            var recipes = level.CookingRecipes;
            if (recipes.EvaluateCrafting(grid, 3) is not { } crafting)
            {
                level.BroadcastLevelEvent(FailEvent, position, 0);
                return;
            }

            outputs = [crafting.Output, .. crafting.Remainders.OfType<ItemStack>()];
            for (var slot = 0; slot < CrafterContainer.GridSize; slot++)
            {
                if (crafter.GetItem(slot) is { Count: > 0 } item)
                    crafter.SetItem(slot, item.Count > 1 ? new ItemStack(item, item.Count - 1) : null);
            }

            crafter.CraftingTicksRemaining = CraftingTicks;
            entity.SetChanged();
        }

        lock (level.Fluids.TickLock)
        {
            if (updates.GetBlock(position) is var current && current.RegistryId == BlockIds.Crafter)
                updates.Set(position, current.WithProperty("crafting", true), BlockUpdateFlags.Clients);
        }

        var front = Front(state);
        foreach (var output in outputs)
            Eject(level, position, front, output);

        await Events.MainEventHandler.RefreshViewersAsync(crafter);
    }

    // Vanilla's CrafterBlock.dispenseItem: into the container in front, one at a time into another crafter, then
    // dropped out the front with the crafter's sound and particles.
    private static void Eject(AbstractLevel level, Vector position, BlockFace front, ItemStack output)
    {
        ItemStack? left = new ItemStack(output, output.Count);

        if (level.GetItemAccess(position.Offset(front), front.Opposite()) is IItemAccess target)
        {
            if (level.BlockUpdates.GetBlockEntity(position.Offset(front)) is CrafterBlockEntity)
            {
                while (left is { Count: > 0 } && target.Insert(new ItemStack(left, 1)) is null)
                    left = left.Count > 1 ? new ItemStack(left, left.Count - 1) : null;
            }
            else
            {
                left = target.Insert(left);
            }
        }

        if (left is not { Count: > 0 })
            return;

        DispenserComponent.DropItem(level, position, front, left);
        level.BroadcastLevelEvent(CraftEvent, position, 0);
        level.BroadcastLevelEvent(ParticlesEvent, position, (int)front);
    }

    // The face the crafter's front is on, the first part of its orientation.
    private static BlockFace Front(IBlock state)
    {
        var orientation = state.GetProperty("orientation") ?? "north_up";
        return FeatureHelpers.ParseFace(orientation[..orientation.IndexOf('_')]);
    }
}
