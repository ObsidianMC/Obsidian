using Microsoft.Extensions.Logging;
using Obsidian.API.BlockEntities;
using Obsidian.API.Inventory;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Plugins;
using System.Threading;

namespace Obsidian.WorldData;

/// <summary>
/// The level's side of block entities: creating and removing them as blocks change, ticking them, taking in their
/// changes, and sending their updates.
/// </summary>
/// <remarks>
/// A plugin's block entity code runs on the level's tick, in turn with its ticks, its saves and its plugin unloading:
/// its removal (<see cref="BlockEntity.OnRemoved"/>) and its updates (<see cref="BlockEntity.SendUpdate"/>) are
/// queued to it. Creating one for a block set from elsewhere constructs it there, before anything else can reach it.
/// </remarks>
public abstract partial class AbstractLevel
{
    // Vanilla's Containers.dropItemStack spreads drops by the width of an item entity, and tosses them with this
    // spread around their motion.
    private const double ItemEntityWidth = 0.25;
    private const double DropMotionSpread = 0.11485000171139836;

    private LevelBlockEntityHost? blockEntityHost;

    // Containers whose items changed, whose OnItemsChanged runs at the end of the block entity tick.
    private readonly ConcurrentDictionary<ContainerBlockEntity, byte> itemsChanged = new();

    private LevelBlockEntityHost BlockEntityHost =>
        LazyInitializer.EnsureInitialized(ref this.blockEntityHost, () => new LevelBlockEntityHost(this));

    /// <summary>Reads the resident entity for identity checks under the block mutation lock.</summary>
    internal IBlockEntity? GetResidentBlockEntity(Vector position) =>
        this.GetLoadedChunk(position.X.ToChunkCoord(), position.Z.ToChunkCoord())
            ?.GetBlockEntity(position.X, position.Y, position.Z);

    /// <summary>
    /// Places a block entity in the level, which it reaches through <see cref="BlockEntity.Level"/> from then on.
    /// </summary>
    internal void AttachBlockEntity(IBlockEntity? blockEntity)
    {
        if (blockEntity is not BlockEntity placed || placed.Host is not null)
            return;

        var host = this.BlockEntityHost;
        placed.Host = host;
        host.Updates[placed] = true;
        this.QueueLoad(placed);

        // A change announced before it was placed is taken in like the others.
        if (placed.HasPendingChange)
            host.OnChanged(placed);
    }

    /// <summary>
    /// Keeps a chunk's block entity in step with its block after the block at <paramref name="position"/> was set to
    /// <paramref name="block"/>: the block entity stays when the block has its type, and otherwise is removed, and the
    /// block's own type, if any, is created.
    /// </summary>
    /// <returns>
    /// The block entity removed, whose removal side effects the caller runs with
    /// <see cref="RunBlockEntityRemovalAsync"/> (or queues with <see cref="QueueBlockEntityRemoval"/>) once it let go
    /// of its locks; or null.
    /// </returns>
    internal BlockEntity? UpdateBlockEntity(
        IChunk chunk, Vector position, IBlock block, BlockEntity? prepared = null)
    {
        // The flag is much cheaper than the type lookup, and blocks without it have no type.
        var type = block.HasBlockEntity() ? block.BlockEntityType() : null;
        var existing = chunk.GetBlockEntity(position.X, position.Y, position.Z);

        if (prepared is null && existing is not null
            && string.Equals(existing.Id, type, StringComparison.OrdinalIgnoreCase))
            return null;

        BlockEntity? removed = null;
        if (existing is not null)
        {
            chunk.RemoveBlockEntity(position.X, position.Y, position.Z);

            if (existing is BlockEntity gone)
            {
                gone.IsRemoved = true;
                removed = gone;
            }
        }

        if (type is not null)
        {
            var created = prepared ?? BlockEntityNbt.Create(type, position, logger: this.Logger);
            this.AttachBlockEntity(created);
            chunk.SetBlockEntity(position.X, position.Y, position.Z, created);
            if (prepared is null)
                this.KeepIfUnloading(chunk, created);
        }

        return removed;
    }

    /// <summary>
    /// Runs what removing a block entity's block does, like vanilla's <c>preRemoveSideEffects</c>: the block
    /// entity's <see cref="BlockEntity.OnRemoved"/>, and a container's items dropping, with the loot it was waiting
    /// for. Call it holding no lock.
    /// </summary>
    /// <param name="removed">The block entity removed.</param>
    /// <param name="onLevelTick">
    /// Whether the caller runs on the level's tick, where a plugin's <see cref="BlockEntity.OnRemoved"/> runs at once;
    /// elsewhere it's queued to the tick, and its container stops taking clicks meanwhile.
    /// </param>
    internal async ValueTask RunBlockEntityRemovalAsync(BlockEntity removed, bool onLevelTick = false)
    {
        if (removed.Owner is not PluginContainer plugin)
        {
            removed.OnRemoved();
            await this.RemoveContentsAsync(removed);
            return;
        }

        if (onLevelTick)
        {
            this.CallPlugin(removed, plugin, static blockEntity => blockEntity.OnRemoved());
            await this.RemoveContentsAsync(removed);
            return;
        }

        if (removed is ContainerBlockEntity container)
        {
            using (await container.Inventory.LockAsync())
                container.Inventory.TryRetire();
        }

        // Unloading: the plugin's code no longer runs, but what the block entity held still drops.
        var hold = plugin.TryHold();
        if (hold is null)
        {
            await this.RemoveContentsAsync(removed);
            return;
        }

        this.EnqueueEntityAction(async () =>
        {
            using (hold)
            {
                this.CallPlugin(removed, plugin, static blockEntity => blockEntity.OnRemoved());
                await this.RemoveContentsAsync(removed);
            }
        });
    }

    /// <summary>
    /// Queues a removed block entity's side effects (see <see cref="RunBlockEntityRemovalAsync"/>) to the level's
    /// next tick, for a caller that can't let go of its locks first, like a fluid tick or a feature placed in the
    /// running level.
    /// </summary>
    internal void QueueBlockEntityRemoval(BlockEntity removed) =>
        this.EnqueueEntityAction(() => this.RunBlockEntityRemovalAsync(removed, onLevelTick: true));

    /// <summary>
    /// Ticks the block entities of the chunks that tick, like vanilla's block entity tickers: those whose block still
    /// has their type, after the pending <see cref="BlockEntity.OnLoad"/> calls. Then runs the level's tickers (see
    /// <see cref="AddTicker"/>), takes in the changes block entities announced (see
    /// <see cref="BlockEntity.SetChanged"/>) and sends the updates they asked for.
    /// </summary>
    internal async ValueTask TickBlockEntitiesAsync()
    {
        var revision = BlockEntityTypesRegistry.Revision;
        var chunks = this.Regions.Values
            .SelectMany(region => region.GeneratedChunks())
            .OfType<Chunk>()
            .ToList();

        foreach (var chunk in chunks)
        {
            if (chunk.RestoredBlockEntityRevision != revision)
                this.RestoreKeptBlockEntities(chunk, revision);
        }

        this.RunPendingLoads();

        foreach (var chunk in chunks)
        {
            if (chunk.BlockEntities.IsEmpty || !this.IsMobTicking(new VectorD(chunk.X * 16 + 8, 0, chunk.Z * 16 + 8)))
                continue;

            foreach (var blockEntity in chunk.BlockEntities.Values.OfType<BlockEntity>())
            {
                var position = blockEntity.BlockPosition;
                var type = chunk.GetBlock(position.X, position.Y, position.Z).BlockEntityType();

                if (blockEntity.IsRemoved || !string.Equals(type, blockEntity.Id, StringComparison.OrdinalIgnoreCase))
                    continue;

                this.AttachBlockEntity(blockEntity);
                this.RunLoad(blockEntity);
                await this.TickBlockEntityAsync(blockEntity);

                // A plugin's container is saved as of its last change, which players make by moving its items. Its
                // own changes made without the inventory's lock are heard here too.
                if (blockEntity is ContainerBlockEntity { SavesOnAnyThread: false } container)
                {
                    var items = container.Inventory.Revision;
                    if (items != container.SavedRevision)
                        container.SetChanged();

                    if (items != container.NotifiedRevision)
                        this.QueueItemsChanged(container);
                }

                if (blockEntity is ContainerBlockEntity watched)
                    this.UpdateComparatorsOf(watched);
            }
        }

        await this.RunTickersAsync();

        await this.FlushItemAccessChangesAsync();
        this.RunItemsChanged();
        this.TakeBlockEntityChanges();
        this.SendBlockEntityUpdates();
    }

    /// <summary>
    /// Queues a container's <see cref="ContainerBlockEntity.OnItemsChanged"/> to the end of the block entity tick,
    /// from any thread and under any lock.
    /// </summary>
    private void QueueItemsChanged(ContainerBlockEntity container) => this.itemsChanged.TryAdd(container, 0);

    /// <summary>
    /// Calls <see cref="ContainerBlockEntity.OnItemsChanged"/> once on each container queued whose items changed since
    /// its last call, inside a call into its plugin and holding no lock. Runs before the changes are taken in, so
    /// that what the call saves (see <see cref="BlockEntity.SetChanged"/>) is saved with this tick.
    /// </summary>
    private void RunItemsChanged()
    {
        foreach (var container in this.itemsChanged.Keys)
        {
            // One still waiting for its OnLoad hears of its items after it, on a later tick.
            if (container.LoadPending && !container.IsRemoved)
                continue;

            if (!this.itemsChanged.TryRemove(container, out _) || container.IsRemoved)
                continue;

            // Read before the call, so that a change made meanwhile, by a click or the call itself, calls it again.
            var revision = container.Inventory.Revision;
            if (revision == container.NotifiedRevision)
                continue;

            container.NotifiedRevision = revision;
            this.CallPlugin(container, container.Owner as PluginContainer,
                static blockEntity => ((ContainerBlockEntity)blockEntity).OnItemsChanged());
        }
    }

    /// <summary>
    /// Takes in the changes block entities announced: the data a plugin's block entity saves is a copy of what its
    /// <see cref="BlockEntity.Save"/> wrote now, on the level's thread, so that the plugin changing what it wrote
    /// later doesn't change what's saved. Like vanilla's <c>setChanged</c>, the comparators reading a changed block
    /// entity's block hear of it then (a container's hear when its items move, see <c>UpdateComparatorsOf</c>).
    /// </summary>
    internal void TakeBlockEntityChanges()
    {
        if (this.blockEntityHost is not LevelBlockEntityHost host)
            return;

        List<Vector>? outputsChanged = null;

        while (host.Changed.TryDequeue(out var blockEntity))
        {
            if (!blockEntity.TakeChange() || blockEntity.IsRemoved || blockEntity.SavesOnAnyThread)
                continue;

            var plugin = blockEntity.Owner as PluginContainer;
            using var call = plugin?.TryEnter();

            if (plugin is not null && call is null)
                continue;

            var revision = (blockEntity as ContainerBlockEntity)?.Inventory.Revision;

            try
            {
                blockEntity.SavedData = DataBlockEntity.Copy(BlockEntityNbt.SaveData(blockEntity));
                blockEntity.ShapeData = blockEntity.GetShapes();
            }
            catch (Exception ex)
            {
                Log.BlockEntityFailed(this.Logger, ex, blockEntity.Id, blockEntity.BlockPosition);
                continue;
            }

            if (blockEntity is ContainerBlockEntity container && revision is long saved)
                container.SavedRevision = saved;
            else
                (outputsChanged ??= []).Add(blockEntity.BlockPosition);
        }

        // Outside the calls into the plugins: the comparators ask the blocks' analog output in calls of their own.
        foreach (var position in outputsChanged ?? [])
            this.BlockUpdates.OnContainerChanged(position);
    }

    /// <summary>
    /// Sends the updates block entities asked for since the last tick (see <see cref="BlockEntity.SendUpdate"/>), like
    /// vanilla's <c>ClientboundBlockEntityDataPacket</c>, to the players who have the chunk: what
    /// <see cref="BlockEntity.GetUpdateData"/> returns now, copied, which chunk packets send from then on too.
    /// </summary>
    private void SendBlockEntityUpdates()
    {
        if (this.blockEntityHost is not LevelBlockEntityHost host)
            return;

        foreach (var blockEntity in host.Updates.Keys)
        {
            if (!host.Updates.TryRemove(blockEntity, out var initial))
                continue;

            if (blockEntity.IsRemoved)
                continue;

            if (!this.RefreshBlockEntitySnapshot(blockEntity))
                continue;

            var data = blockEntity.ClientData;
            var type = BlockEntityTypesRegistry.NetworkId(blockEntity.Id);
            if (data is null || type < 0)
                continue;

            var packet = new BlockEntityDataPacket
            {
                Position = blockEntity.BlockPosition,
                Type = type,
                NBTData = data
            };

            foreach (var player in this.PlayersInRange(blockEntity.BlockPosition))
            {
                // Placement can originate on another thread; always recreate the state before its initial data.
                if (initial && this.GameEventBlock(blockEntity.BlockPosition) is IBlock block)
                    player.SendPacket(new BlockUpdatePacket(blockEntity.BlockPosition, block.GetHashCode()));

                player.SendPacket(packet);
            }
        }
    }

    /// <summary>
    /// Publishes the current shapes and client data on the level tick, including between queued breaks.
    /// </summary>
    internal bool RefreshBlockEntitySnapshot(BlockEntity blockEntity) =>
        !blockEntity.IsRemoved && this.CallPlugin(blockEntity, blockEntity.Owner as PluginContainer, current =>
        {
            current.ShapeData = current.GetShapes();
            current.ClientData = current.GetUpdateData() is NbtCompound update ? DataBlockEntity.Copy(update) : null;
        });

    private async ValueTask TickBlockEntityAsync(BlockEntity blockEntity)
    {
        if (blockEntity.Owner is not PluginContainer plugin)
        {
            await blockEntity.TickAsync();
            return;
        }

        // A call into the plugin, so that it can't be released meanwhile; and its failures stay its own.
        using var call = plugin.TryEnter();
        if (call is null)
            return;

        try
        {
            await blockEntity.TickAsync();
        }
        catch (Exception ex)
        {
            Log.BlockEntityFailed(this.Logger, ex, blockEntity.Id, blockEntity.BlockPosition);
        }
    }

    /// <summary>
    /// Runs block entity code synchronously: the server's at once, a plugin's inside a call into its plugin, with its
    /// failures logged.
    /// </summary>
    /// <returns>Whether the code ran without failing: not when the plugin is unloading.</returns>
    private bool CallPlugin(BlockEntity blockEntity, PluginContainer? plugin, Action<BlockEntity> code)
    {
        if (plugin is null)
        {
            code(blockEntity);
            return true;
        }

        using var call = plugin.TryEnter();
        if (call is null)
            return false;

        try
        {
            code(blockEntity);
            return true;
        }
        catch (Exception ex)
        {
            Log.BlockEntityFailed(this.Logger, ex, blockEntity.Id, blockEntity.BlockPosition);
            return false;
        }
    }

    /// <summary>
    /// Creates the plugins' block entities a complete chunk keeps as saved data (see <see cref="DataBlockEntity"/>)
    /// once their types are bound: those generation placed or the chunk loaded with, and those kept while their
    /// plugins were gone (see <see cref="PluginBlockEntities"/>). On the level's tick, in turn with plugins
    /// unloading, whenever the chunk is new to the block entity type registry's <paramref name="revision"/>.
    /// </summary>
    private void RestoreKeptBlockEntities(Chunk chunk, int revision)
    {
        chunk.RestoredBlockEntityRevision = revision;

        foreach (var kept in chunk.BlockEntities.Values.OfType<DataBlockEntity>())
        {
            if (!BlockEntityTypesRegistry.TryGet(kept.Id, out var type) || type.Binding is null)
                continue;

            var position = kept.BlockPosition;
            var blockType = chunk.GetBlock(position.X, position.Y, position.Z).BlockEntityType();

            if (blockType != type.Identifier)
                continue;

            var created = BlockEntityNbt.Create(type.Identifier, position, kept.SnapshotData(), this.Logger);
            if (created is DataBlockEntity)
                continue;

            if (chunk.ReplaceBlockEntity(kept, created))
            {
                kept.IsRemoved = true;
                this.AttachBlockEntity(created);
            }
        }
    }

    /// <summary>Finishes a placement that raced the plugin's unloading sweep, after transferring its items.</summary>
    internal void CompleteBlockEntityPlacement(IChunk chunk, BlockEntity? entity)
    {
        if (entity is not null)
            this.KeepIfUnloading(chunk, entity);
    }

    // A plugin's block entity created as its plugin started unloading may be stored after the unloading swept the
    // chunk (see PluginBlockEntities): it's kept as its data then, like the others.
    private void KeepIfUnloading(IChunk chunk, BlockEntity created)
    {
        if (created.Owner is PluginContainer { IsClosing: true } plugin && chunk is Chunk stored)
            PluginBlockEntities.Keep(this, stored, created, plugin);
    }

    /// <summary>
    /// The removal side effects that aren't the block entity's own code: a container's items drop, with the loot it
    /// was waiting for, and the windows showing it close.
    /// </summary>
    /// <remarks>
    /// Its items are taken out under the container's lock, which clicks hold, in the step that retires it, so that no
    /// click takes or copies an item that drops; the items drop after, holding no lock.
    /// </remarks>
    private async ValueTask RemoveContentsAsync(BlockEntity removed)
    {
        switch (removed)
        {
            case ContainerBlockEntity container:
                var contents = new List<ItemStack>();
                using (await container.Inventory.LockAsync())
                    container.Inventory.TryRetire();

                if (container.DropsContents)
                {
                    // Loot may load chunks: keep the retired inventory frozen, but hold no click or tick lock.
                    this.UnpackLootTable(container, null, removing: true);
                    using (await container.Inventory.LockAsync())
                        TakeContents(container.Inventory, contents);
                }

                await CloseWindowsAsync(container.Inventory);

                foreach (var item in contents)
                    this.DropItemStack(container.BlockPosition, item);

                if (container is FurnaceBlockEntity furnace)
                {
                    int experience;
                    using (furnace.Inventory.Lock())
                        experience = furnace.TakeExperience(this.CookingRecipes, Globals.Random.NextSingle);

                    this.DropCookingExperience((VectorD)furnace.BlockPosition, experience);
                }

                break;
            case DataBlockEntity data when data.KeptContainer() is ContainerBlockEntity kept:
                await this.RemoveContentsAsync(kept);
                break;
            case DataBlockEntity { Id: "minecraft:decorated_pot" } pot:
                // Vanilla's pot spills its item separately; its block loot copies only pot_decorations.
                NbtCompound potData;
                lock (pot.Data)
                {
                    potData = pot.SnapshotData();
                    pot.Remove("item");
                }

                if (potData.TryGetTag<NbtCompound>("item", out var itemTag)
                    && itemTag.ItemFromNbt() is ItemStack potItem && !potItem.IsAir && potItem.Count > 0)
                {
                    this.DropItemStack(pot.BlockPosition, potItem);
                }

                break;
            case DataBlockEntity { DropsItems: true } data:
                this.DropSavedItems(data);
                break;
        }
    }

    /// <summary>
    /// Closes the windows players have open on <paramref name="inventory"/>, or on a container made of it, like
    /// vanilla's menus closing once their block is gone.
    /// </summary>
    /// <param name="inventory">The inventory, retired first so that no click applies meanwhile.</param>
    /// <param name="unloading">The plugin whose unloading closes them, if any (see
    /// <see cref="Player.CloseContainerAsync"/>).</param>
    internal static async ValueTask CloseWindowsAsync(BaseContainer inventory, PluginContainer? unloading = null)
    {
        foreach (var viewer in inventory.GetViewers())
        {
            if (viewer is Player player
                && player.OpenedContainer is BaseContainer shown
                && shown.SharesSlotsWith(inventory))
            {
                await player.CloseContainerAsync(unloading);
            }
        }
    }

    // Vanilla's Containers.dropContents takes each stack out of the container, in slot order, to drop it.
    private static void TakeContents(BaseContainer inventory, List<ItemStack> contents)
    {
        for (var slot = 0; slot < inventory.Size; slot++)
        {
            var item = inventory[slot];
            if (item is null || item.IsAir || item.Count <= 0)
                continue;

            inventory.SetItem(slot, null);
            contents.Add(item);
        }
    }

    // The saved items of a block entity kept as data, dropped like a container's, in slot order.
    private void DropSavedItems(DataBlockEntity blockEntity)
    {
        var data = blockEntity.SnapshotData();
        if (!data.TryGetTag<NbtList>("Items", out var items))
            return;

        var stacks = items
            .OfType<NbtCompound>()
            .OrderBy(item => item.TryGetTagValue<byte>("Slot", out var slot) ? slot : 0)
            .Select(item => item.ItemFromNbt())
            .OfType<ItemStack>();

        foreach (var stack in stacks)
        {
            if (!stack.IsAir && stack.Count > 0)
                this.DropItemStack(blockEntity.BlockPosition, stack);
        }
    }

    /// <summary>
    /// Vanilla's <c>Containers.dropItemStack</c>: the stack drops at a random point of the block's lower part, in
    /// item entities of 10 to 30 items, each tossed up with a little random motion. Draws from the level's random in
    /// vanilla's order, the motion the item entity's constructor draws included.
    /// </summary>
    private void DropItemStack(Vector position, ItemStack stack)
    {
        var random = this.GrowthRandom;
        var spread = 1 - ItemEntityWidth;
        var x = position.X + random.NextDouble() * spread + ItemEntityWidth / 2;
        var y = position.Y + random.NextDouble() * spread;
        var z = position.Z + random.NextDouble() * spread + ItemEntityWidth / 2;

        var remaining = stack.Count;
        while (remaining > 0)
        {
            var count = Math.Min(remaining, random.NextInt(21) + 10);
            remaining -= count;

            // The item entity's own motion, which the drop replaces.
            random.NextDouble();
            random.NextDouble();

            var motion = new VectorD(
                random.Triangle(0.0, DropMotionSpread),
                random.Triangle(0.2, DropMotionSpread),
                random.Triangle(0.0, DropMotionSpread));

            var item = new ItemEntity
            {
                Level = this,
                EntityId = Server.GetNextEntityId(),
                Position = new VectorD(x, y, z),
                Item = new ItemStack(stack, count),
                Motion = motion
            };

            if (this.TryAddEntity(item))
                item.SpawnEntity(new Velocity(motion.X, motion.Y, motion.Z));
        }
    }

    /// <summary>
    /// What the level's block entities reach of it.
    /// </summary>
    private sealed class LevelBlockEntityHost(AbstractLevel level) : BlockEntityHost, IContainerItemsHost
    {
        /// <summary>
        /// The block entities whose changes the level takes in at the end of its tick.
        /// </summary>
        public ConcurrentQueue<BlockEntity> Changed { get; } = new();

        /// <summary>
        /// The block entities whose updates the level sends at tick end; true also sends their initial state first.
        /// </summary>
        public ConcurrentDictionary<BlockEntity, bool> Updates { get; } = new();

        public override ILevel Level => level;

        public override IServer? ServerOf(BlockEntity blockEntity) =>
            (blockEntity.Owner as PluginContainer)?.HostServices?.Server;

        public override void OnChanged(BlockEntity blockEntity) => this.Changed.Enqueue(blockEntity);

        public bool HoldsTickLock => level.HoldsTickLock;

        public int SlotLimit(ContainerBlockEntity container, int slot, ItemStack stack)
        {
            var limit = 0;
            level.CallPlugin(container, container.Owner as PluginContainer,
                _ => limit = container.SlotLimit(slot, stack));

            return limit;
        }

        public bool PrepareItems(ContainerBlockEntity container) => level.PrepareContainerItems(container);

        public void OnItemsChanged(ContainerBlockEntity container) => level.OnContainerItemsChanged(container);

        public void QueueItemsChanged(ContainerBlockEntity container) => level.QueueItemsChanged(container);

        public override void SendUpdate(BlockEntity blockEntity)
        {
            if (!blockEntity.IsRemoved)
                this.Updates.TryAdd(blockEntity, false);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "Block entity {Type} at {Position} failed")]
        public static partial void BlockEntityFailed(ILogger logger, Exception exception, string type, Vector position);
    }
}
