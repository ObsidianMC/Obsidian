using Obsidian.API.BlockEntities;
using Obsidian.API.Blocks;
using Obsidian.API.Containers;
using Obsidian.API.Inventory;
using Obsidian.WorldData.BlockNotifications;
using Obsidian.WorldData.BlockTicks;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// The crafter's block, like vanilla's <c>CrafterBlock</c>: a rising redstone signal crafts what its grid holds four game
/// ticks later, putting the result in the container it faces or dropping it, with the ingredients' remainders.
/// </summary>
internal sealed class RedstoneCrafter(IRedstoneLevel level, RedstoneEngine engine) : INeighborChangedBehavior
{
    public const int BlockId = BlockIds.Crafter;

    private const int CraftDelay = 4;

    // How long the crafter shows itself crafting (vanilla's CrafterBlockEntity crafting ticks).
    private const int CraftingTicks = 6;

    // Vanilla's LevelEvent.SOUND_CRAFTER_CRAFT, SOUND_CRAFTER_FAIL and PARTICLES_CRAFTER_CRAFT.
    private const int CraftEvent = 1049;
    private const int FailEvent = 1050;
    private const int ParticlesEvent = 2010;

    public static void Register(IRedstoneLevel level, LevelBlockTicks ticks, NeighborBehaviors neighbors, RedstoneEngine engine)
    {
        var crafter = new RedstoneCrafter(level, engine);
        neighbors.Register(BlockId, crafter);
        ticks.Register(BlockId, (position, _) => level.CraftCrafter(position));
    }

    public async ValueTask OnNeighborChangedAsync(NeighborChange change, IBlockChangeWorld world)
    {
        var position = change.Position;
        var state = change.State;
        var powered = engine.HasNeighborSignal(position);
        var triggered = state.GetProperty("triggered") == "true";

        if (powered && !triggered)
        {
            level.ScheduleBlockTick(position, state, CraftDelay);
            await level.WriteAsync(position, state.WithProperty("triggered", true), notifyNeighbors: false);
            level.SetCrafterTriggered(position, true);
        }
        else if (!powered && triggered)
        {
            await level.WriteAsync(position, state.WithProperty("triggered", false), notifyNeighbors: false);
            level.SetCrafterTriggered(position, false);
        }
    }

    // Vanilla's dispenseFrom: the grid's recipe, its disabled slots counting as empty, crafts once; the result and the
    // ingredients' remainders go out the front, and each ingredient is used up by one.
    internal static async ValueTask CraftAsync(AbstractLevel level, Vector position)
    {
        var updates = level.BlockUpdates;
        var state = updates.GetBlock(position);
        if (state.RegistryId != BlockId || updates.GetBlockEntity(position) is not CrafterBlockEntity entity)
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
            if (updates.GetBlock(position) is var current && current.RegistryId == BlockId)
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

        RedstoneDispenser.DropItem(level, position, front, left);
        level.BroadcastLevelEvent(CraftEvent, position, 0);
        level.BroadcastLevelEvent(ParticlesEvent, position, (int)front);
    }

    // The face the crafter's front is on, the first part of its orientation.
    internal static BlockFace Front(IBlock state)
    {
        var orientation = state.GetProperty("orientation") ?? "north_up";
        return FeatureHelpers.ParseFace(orientation[..orientation.IndexOf('_')]);
    }
}

/// <summary>
/// Vanilla <c>CrafterBlock.getStateForPlacement</c>: it faces away from the player; its top faces up, or, facing up or
/// down, the way the player looks. It starts triggered when a signal already reaches it.
/// </summary>
internal sealed class CrafterPlacement : IBlockBehavior
{
    public IBlock? GetStateForPlacement(BlockPlaceContext context)
    {
        var front = context.NearestLookDirection.Opposite();
        var top = front switch
        {
            BlockFace.Down => context.HorizontalDirection.Opposite(),
            BlockFace.Up => context.HorizontalDirection,
            _ => BlockFace.Up
        };

        return context.Block
            .WithProperty("orientation", $"{FeatureHelpers.FaceName(front)}_{FeatureHelpers.FaceName(top)}")
            .WithProperty("triggered", context.Level.HasNeighborSignal(context.Position));
    }
}
