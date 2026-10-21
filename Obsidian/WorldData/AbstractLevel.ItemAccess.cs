using Microsoft.Extensions.Logging;
using Obsidian.API.BlockEntities;
using Obsidian.API.Inventory;
using Obsidian.Entities;
using Obsidian.Entities.AI;
using Obsidian.Events;
using Obsidian.Plugins;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData;

/// <summary>
/// The level's side of item access (see <see cref="IItemAccess"/>): finding the access a block gives from a side,
/// and what follows a change made through it.
/// </summary>
public abstract partial class AbstractLevel
{
    // Containers changed through item access since the last block entity tick ended, whose windows and comparators
    // are brought up to date then.
    private readonly ConcurrentDictionary<ContainerBlockEntity, byte> itemAccessChanges = new();

    // Loot requested under the tick or item transfer lock, rolled on the next tick without those locks.
    private readonly ConcurrentDictionary<ContainerBlockEntity, byte> pendingLoot = new();

    /// <inheritdoc/>
    /// <remarks>
    /// The block and its block entity are read under the tick lock, so that a chest's halves are read together;
    /// a plugin's block or block entity is asked inside a call into its plugin, and the access it gives is called
    /// inside calls into the plugin too, giving nothing once it's unloading.
    /// </remarks>
    public IItemAccess? GetItemAccess(Vector position, BlockFace? side) =>
        this.BlockItemAccess(position, side) ?? this.VehicleItemAccess(position);

    // Vanilla's HopperBlockEntity.getEntityContainer: with no container block there, a chest boat or a minecart with a
    // chest or hopper whose box reaches into the block, a random one of them if there are several.
    private IItemAccess? VehicleItemAccess(Vector position)
    {
        var cube = new BoundingBox((VectorD)position, (VectorD)position + new VectorD(1, 1, 1));
        var vehicles = this.GetEntitiesInRange((VectorD)position + new VectorD(0.5, 0.5, 0.5), 3)
            .OfType<VehicleEntity>()
            .Where(vehicle => vehicle is IVehicleContainer
                && !vehicle.IsRemoved
                && MobTerrain.Overlaps(cube, vehicle.Dimension.CreateBBFromPosition(vehicle.Position)))
            .ToArray();

        return vehicles.Length == 0
            ? null
            : ((IVehicleContainer)vehicles[vehicles.Length == 1 ? 0 : Random.Shared.Next(vehicles.Length)]).ItemAccess;
    }

    private IItemAccess? BlockItemAccess(Vector position, BlockFace? side)
    {
        lock (this.Fluids.TickLock)
        {
            var updates = this.BlockUpdates;
            var state = updates.GetBlock(position);
            if (state.IsAir)
                return null;

            if (this.PluginBlockAccess(updates, position, state, side) is IItemAccess pluginAccess)
                return pluginAccess;

            var access = this.BlockEntityAccess(updates.GetBlockEntity(position), side);
            if (access is null || !IsChest(state) || ChestPartner(updates, position, state) is not Vector partner)
                return access;

            if (this.BlockEntityAccess(updates.GetBlockEntity(partner), side) is not IItemAccess other)
                return access;

            // Vanilla's ChestBlock.getContainer: the right half's slots come first.
            return state.GetProperty("type") == "right"
                ? new CombinedItemAccess(access, other)
                : new CombinedItemAccess(other, access);
        }
    }

    /// <summary>
    /// Brings up to date what follows the changes made through item access since the last call: the comparators
    /// reading the containers, and the windows players have open on them.
    /// </summary>
    internal async ValueTask FlushItemAccessChangesAsync()
    {
        foreach (var container in this.itemAccessChanges.Keys)
        {
            if (!this.itemAccessChanges.TryRemove(container, out _) || container.IsRemoved)
                continue;

            this.UpdateComparatorsOf(container);
            await MainEventHandler.RefreshViewersAsync(container.Inventory);
        }
    }

    /// <summary>
    /// Rolls a container's pending loot before item access reaches it (see
    /// <see cref="IContainerItemsHost.PrepareItems"/>): at once, or on the next tick when holding the tick or item
    /// transfer lock. Rolling loot may load chunks, so it must not hold either lock.
    /// </summary>
    private bool PrepareContainerItems(ContainerBlockEntity container)
    {
        if (container.LootTable is null)
            return true;

        if (!this.Fluids.TickLock.IsHeldByCurrentThread
            && !System.Threading.Monitor.IsEntered(ItemEntity.TransferLock))
        {
            this.UnpackLootTable(container, opener: null);
            return true;
        }

        if (this.pendingLoot.TryAdd(container, 0))
        {
            this.EnqueueEntityAction(() =>
            {
                this.pendingLoot.TryRemove(container, out _);
                if (!container.IsRemoved)
                    this.UnpackLootTable(container, opener: null);

                return ValueTask.CompletedTask;
            });
        }

        return false;
    }

    // A plugin block's own access, asked like its other hooks.
    private PluginItemAccess? PluginBlockAccess(
        LevelBlockUpdates updates,
        Vector position,
        IBlock state,
        BlockFace? side)
    {
        if (!BlockBehaviors.TryEnter(state, out var behavior, out var call)
            || !BlocksRegistry.TryGetPluginBlock(state.GetHashCode(), out var type)
            || type.Owner is not PluginContainer plugin)
        {
            call?.Dispose();
            return null;
        }

        using (call)
        {
            try
            {
                var context = new ItemAccessContext
                {
                    Level = updates,
                    Position = position,
                    State = state,
                    Side = side
                };

                return behavior.GetItemAccess(context) is IItemAccess access
                    ? new PluginItemAccess(access, plugin, this.ItemAccessFailed)
                    : null;
            }
            catch (Exception ex)
            {
                this.ItemAccessFailed(ex);
                return null;
            }
        }
    }

    // A block entity's access: a plugin's asked, and called, inside calls into its plugin.
    private IItemAccess? BlockEntityAccess(IBlockEntity? blockEntity, BlockFace? side)
    {
        if (blockEntity is not IItemAccessProvider provider)
            return null;

        if (blockEntity is BlockEntity { IsRemoved: true })
            return null;

        // Placed in the level, so that its access rolls its loot and reports its changes.
        this.AttachBlockEntity(blockEntity);

        if ((blockEntity as BlockEntity)?.Owner is not PluginContainer plugin)
            return provider.GetItemAccess(side);

        using var call = plugin.TryEnter();
        if (call is null)
            return null;

        try
        {
            return provider.GetItemAccess(side) is IItemAccess access
                ? new PluginItemAccess(access, plugin, this.ItemAccessFailed)
                : null;
        }
        catch (Exception ex)
        {
            this.ItemAccessFailed(ex);
            return null;
        }
    }

    private void ItemAccessFailed(Exception exception) => Log.ItemAccessFailed(this.Logger, exception);

    // Vanilla's chests, whose halves make one double chest: ChestBlock and its subclasses.
    private static bool IsChest(IBlock state) =>
        state.BlockClass() is "ChestBlock" or "TrappedChestBlock" or "CopperChestBlock" or "WeatheringCopperChestBlock";

    /// <summary>
    /// The other half of the double chest <paramref name="state"/> is half of, like vanilla's
    /// <c>ChestBlock.getConnectedDirection</c> and its block combiner: clockwise of a left half's facing,
    /// counterclockwise of a right half's, the same block facing the same way with the other type.
    /// </summary>
    private static Vector? ChestPartner(LevelBlockUpdates updates, Vector position, IBlock state)
    {
        var type = state.GetProperty("type");
        var facingName = state.GetProperty("facing");
        if (type is not ("left" or "right") || facingName is null)
            return null;

        var facing = FeatureHelpers.ParseFace(facingName);
        var partner = position.Offset(type == "left" ? facing.ClockWise() : facing.CounterClockWise());
        var other = updates.GetBlock(partner);

        var matches = other.RegistryId == state.RegistryId
            && other.GetProperty("facing") == facingName
            && other.GetProperty("type") is ("left" or "right")
            && other.GetProperty("type") != type;

        return matches ? partner : null;
    }

    /// <summary>
    /// What the level does for its block entities' item access (see <see cref="IContainerItemsHost"/>).
    /// </summary>
    private bool HoldsTickLock => this.Fluids.TickLock.IsHeldByCurrentThread;

    private void OnContainerItemsChanged(ContainerBlockEntity container)
    {
        this.itemAccessChanges.TryAdd(container, 0);

        // Also for a call that didn't take the inventory's lock through Lock (see ContainerItemAccess.WithoutWaiting).
        this.QueueItemsChanged(container);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "A plugin's item access failed")]
        public static partial void ItemAccessFailed(ILogger logger, Exception exception);
    }
}

/// <summary>
/// Two (or more) accesses as one, their slots one after the other: a double chest's halves.
/// </summary>
internal sealed class CombinedItemAccess(params IItemAccess[] parts) : IItemAccess
{
    public int SlotCount => parts.Sum(part => part.SlotCount);

    public ItemStack? GetItem(int slot) => this.Part(ref slot).GetItem(slot);

    public ItemStack? Insert(int slot, ItemStack stack, bool simulate = false) =>
        this.Part(ref slot).Insert(slot, stack, simulate);

    public ItemStack? Extract(int slot, int count, bool simulate = false) =>
        this.Part(ref slot).Extract(slot, count, simulate);

    private IItemAccess Part(ref int slot)
    {
        foreach (var part in parts)
        {
            var count = part.SlotCount;
            if ((uint)slot < (uint)count)
                return part;

            slot -= count;
        }

        throw new ArgumentOutOfRangeException(nameof(slot));
    }
}

/// <summary>
/// A plugin's item access, each call made inside a call into the plugin, with its failures logged: once the plugin
/// is unloading, or when a call fails, it takes and gives nothing.
/// </summary>
internal sealed class PluginItemAccess(IItemAccess inner, PluginContainer plugin, Action<Exception> failed)
    : IItemAccess
{
    public int SlotCount => this.Call(access => access.SlotCount, 0);

    public ItemStack? GetItem(int slot) => this.Call(access => access.GetItem(slot), null);

    public ItemStack? Insert(int slot, ItemStack stack, bool simulate = false) =>
        this.Call(access => access.Insert(slot, stack, simulate), stack);

    public ItemStack? Insert(ItemStack stack, bool simulate = false) =>
        this.Call(access => access.Insert(stack, simulate), stack);

    public ItemStack? Extract(int slot, int count, bool simulate = false) =>
        this.Call(access => access.Extract(slot, count, simulate), null);

    private T Call<T>(Func<IItemAccess, T> call, T fallback)
    {
        using var entered = plugin.TryEnter();
        if (entered is null)
            return fallback;

        try
        {
            return call(inner);
        }
        catch (Exception ex)
        {
            failed(ex);
            return fallback;
        }
    }
}
