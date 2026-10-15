using Obsidian.API.Plugins;
using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.Nbt;
using System.Threading;

namespace Obsidian.API.BlockEntities;

/// <summary>
/// The data and behaviour the world keeps for one block besides its state, like vanilla's and NeoForge's block
/// entities: a chest's items, a spawner's timers, or a plugin's machine. Subclass it and register the subclass for
/// blocks with <see cref="IPluginRegistry.RegisterBlockEntity{TBlockEntity}"/>.
/// </summary>
/// <remarks>
/// <para>
/// The server creates a block entity when one of its type's blocks is set, by any means (a player placing it, plugin
/// code, world generation), and removes it when the position changes to a block its type doesn't cover. Loading its
/// chunk loads it from the chunk's saved <c>block_entities</c> through <see cref="Load"/>. A plugin's block entities
/// in chunks being generated or loaded are kept as saved data until the level's next tick once the chunk is
/// complete, which creates and loads them.
/// </para>
/// <para>
/// A plugin's block entity hears when it joins and leaves the level's live set, like NeoForge's block entities:
/// <see cref="OnLoad"/> once it's placed, its chunk loaded or its plugin reloaded, then <see cref="OnRemoved"/> when
/// its block is removed or <see cref="OnChunkUnloaded"/> when its chunk unloads. Its plugin unloading tells it
/// nothing.
/// </para>
/// <para>
/// The server saves what <see cref="Save"/> writes as of the last time the block entity called
/// <see cref="SetChanged"/>, taken at the end of the level's tick: call it whenever its saved data changes, like
/// vanilla's <c>setChanged</c>. The server calls <see cref="Load"/>, <see cref="Save"/>, <see cref="TickAsync"/>,
/// <see cref="GetUpdateData"/>, <see cref="GetShapes"/>, <see cref="CollectComponents"/>, <see cref="OnLoad"/>,
/// <see cref="OnRemoved"/> and <see cref="OnChunkUnloaded"/> on the level's tick (or between two ticks), one at a
/// time, besides creating a new one for a block set elsewhere, which <see cref="ApplyComponents"/> initializes; code
/// that changes it on the level's tick needs no locking. Chunks are saved and sent from copies of what those calls
/// returned.
/// </para>
/// </remarks>
public abstract partial class BlockEntity : IBlockEntity
{
    // 1 from SetChanged until the server took the change in (see TakeChange).
    private int changed;

    /// <inheritdoc/>
    public string Id { get; internal set; } = string.Empty;

    /// <inheritdoc/>
    public Vector BlockPosition { get; internal set; }

    /// <summary>
    /// The level the block entity is in, or null while it's in a chunk that isn't part of a level yet (being
    /// generated), or it was created but not placed. Always null on the client; use the GetClientWorld() extension
    /// in Obsidian.API.Client for the client's separate read-only world view.
    /// </summary>
    public ILevel? Level => this.Host?.Level;

    /// <summary>
    /// The server as the code running now sees it, like event arguments' <c>Server</c>: in the calls into the block
    /// entity's plugin, such as <see cref="TickAsync"/>, the plugin's own <see cref="IServer"/>, which finds its
    /// recipes (<see cref="IServer.FindRecipe"/>) and keeps what's added through it the plugin's. Null while the block
    /// entity isn't in a level, and for the server's own block entities.
    /// </summary>
    public IServer? Server => this.Host?.ServerOf(this) is IServer server ? ServerScope.Resolve(server) : null;

    /// <summary>
    /// Whether the block entity was removed from its level: its block changed to one without its type, its chunk
    /// unloaded, or its plugin unloaded. A removed block entity isn't ticked, saved or sent again.
    /// </summary>
    public bool IsRemoved
    {
        get => Volatile.Read(ref this.removed);
        internal set
        {
            lock (this.liveCallLock)
            {
                this.removalRequested = value;
                if (this.liveCall is null)
                    Volatile.Write(ref this.removed, value);
            }
        }
    }

    /// <summary>
    /// The level side of the block entity, set once it's placed in a level.
    /// </summary>
    internal BlockEntityHost? Host { get; set; }

    /// <summary>
    /// The plugin whose code the block entity runs, if any: the plugin that registered its type, or whose assembly
    /// its class is in. Its code is only called inside a call into that plugin.
    /// </summary>
    internal IPluginContainer? Owner { get; set; }

    /// <summary>
    /// A copy of what <see cref="Save"/> wrote when the server last took a change in, without the id and position,
    /// which the chunk is saved with. Null until then, which saves no data beyond the id and position.
    /// </summary>
    internal NbtCompound? SavedData { get; set; }

    /// <summary>
    /// A copy of what <see cref="GetUpdateData"/> returned when the block entity was created, loaded or last sent an
    /// update, which chunk packets send.
    /// </summary>
    internal NbtCompound? ClientData { get; set; }

    // Published with saved/update data; collision and raycast readers never call plugin code.
    internal volatile BlockShapes? ShapeData;

    /// <summary>
    /// Whether the server may call <see cref="Save"/> and <see cref="GetUpdateData"/> from any thread, when the chunk
    /// is saved or sent, rather than keeping what they returned: true for the server's own block entities.
    /// </summary>
    internal virtual bool SavesOnAnyThread => false;

    /// <summary>
    /// Whether <see cref="SetChanged"/> was called since the server last took a change in.
    /// </summary>
    internal bool HasPendingChange => Volatile.Read(ref this.changed) == 1;

    /// <summary>
    /// Whether the level placed the block entity and hasn't called <see cref="OnLoad"/> yet.
    /// </summary>
    internal bool LoadPending { get; set; }

    /// <summary>
    /// Reads the block entity's saved data, as <see cref="Save"/> wrote it, when its chunk loads, or when a structure
    /// places it with data. Fields missing from <paramref name="tag"/> mean their defaults. On the client it reads the
    /// update data instead (see <see cref="ClientTick"/>).
    /// </summary>
    /// <param name="tag">The saved data, without the id and position.</param>
    protected internal virtual void Load(NbtCompound tag)
    {
    }

    /// <summary>
    /// Writes the block entity's data into <paramref name="tag"/>, which the server saves with the id and position.
    /// </summary>
    /// <param name="tag">An empty compound to write to.</param>
    protected internal virtual void Save(NbtCompound tag)
    {
    }

    /// <summary>
    /// Runs once a tick while the block entity's chunk ticks (is within the simulation distance of a player), like
    /// vanilla's block entity tickers.
    /// </summary>
    protected internal virtual ValueTask TickAsync() => ValueTask.CompletedTask;

    /// <summary>
    /// Runs once a client tick (20 a second) on the Obsidian client, like vanilla's client-side block entity tickers:
    /// for what only players see, such as animations, particles or sounds. The server never calls it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A mod whose type is registered on the client too (with <see cref="IPluginRegistry.RegisterBlockEntity{T}"/>
    /// in <c>ConfigureRegistry</c>) gets a live instance of its class for each of its block entities in the chunks the
    /// client has, created when the server sends it and loaded with <see cref="Load"/> from what
    /// <see cref="GetUpdateData"/> returned rather than saved data: again, on the same instance, on each update. It's
    /// dropped (<see cref="IsRemoved"/>) when its block changes to one without its type, its chunk unloads, the player
    /// leaves the server, or the mod unloads.
    /// </para>
    /// <para>
    /// It runs on the client's game thread while the player is in a world that isn't paused, one block entity at a
    /// time, in turn with its <see cref="Load"/> calls. It may use the block entity's own fields, <see cref="Id"/>,
    /// <see cref="BlockPosition"/>, and client state the mod kept from <c>ConfigureClient</c>; <see cref="Level"/> is
    /// null. The GetClientWorld() extension in Obsidian.API.Client reads neighbouring blocks, synced block entity
    /// data and tracked entities; its live instance and entity queries require this game thread. The view is also
    /// available during client Load, and detached when the instance is dropped. <see cref="SetChanged"/> and
    /// <see cref="SendUpdate"/> do nothing. Keep it short, as the game waits
    /// for it. A tick that throws is logged, and the block entity isn't ticked again until the server updates it.
    /// </para>
    /// </remarks>
    protected internal virtual void ClientTick()
    {
    }

    /// <summary>
    /// The data clients get for the block entity (vanilla's <c>getUpdateTag</c>), with the chunk and from
    /// <see cref="SendUpdate"/>; null, the default, sends none.
    /// </summary>
    /// <remarks>
    /// A client mod's shape and rendering code reads it, and its client instance loads it (see
    /// <see cref="ClientTick"/>), so send only what clients show.
    /// </remarks>
    protected internal virtual NbtCompound? GetUpdateData() => null;

    /// <summary>
    /// Overrides this block's collision and outline boxes from its data, or returns null for the state's shapes.
    /// Called when created or loaded, after a player's break hook, and at tick end after SetChanged or SendUpdate.
    /// Call both when a shape change must be saved and shown to clients; GetUpdateData includes the client's data.
    /// </summary>
    protected internal virtual BlockShapes? GetShapes() => null;

    /// <summary>
    /// Copies the block entity's data that its block's item keeps into <paramref name="item"/>, like vanilla's
    /// <c>collectImplicitComponents</c>: set values of the plugin's own component types on it (see
    /// <see cref="ItemStack.Set"/>), which <see cref="ApplyComponents"/> reads back when the item is placed again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called as the block breaks (by a player or an explosion), before it's removed, on the level's tick or between
    /// two ticks. What it sets goes to the block's loot, like a vanilla block entity's components: a plugin block
    /// without a loot table drops its item with the plugin's component values it set, and a loot table copies them
    /// with <c>minecraft:copy_components</c> (<c>"source": "block_entity"</c>). Not called once the plugin started
    /// unloading.
    /// </para>
    /// <para>
    /// Keep it to reading the block entity's own state: what a container holds still drops as its items. A call that
    /// throws is logged, and the item gets none of the block entity's data.
    /// </para>
    /// </remarks>
    /// <param name="item">A stack of the block's item, to set components on.</param>
    protected internal virtual void CollectComponents(ItemStack item)
    {
    }

    /// <summary>
    /// Reads back what <see cref="CollectComponents"/> copied into the item a player places the block with, like
    /// vanilla's <c>applyImplicitComponents</c>: read the plugin's component values with
    /// <see cref="ItemStack.TryGet"/> and set the block entity's fields from them. The server saves the block entity
    /// afterwards.
    /// </summary>
    /// <remarks>
    /// Called on the new block entity before anything else can reach it, as the block is placed, on any thread;
    /// <see cref="Level"/> is null then. A call that throws is logged, and the block is placed without the data.
    /// </remarks>
    /// <param name="item">A copy of one of the stack the block is placed with.</param>
    protected internal virtual void ApplyComponents(ItemStack item)
    {
    }

    /// <summary>
    /// Called when the block entity's block is removed from the level, before what a container holds drops, like
    /// vanilla's <c>preRemoveSideEffects</c>: drop what it holds, say. Not called when its chunk unloads (see
    /// <see cref="OnChunkUnloaded"/>), or its plugin does. Runs on the level's tick after the removal, which may come
    /// from elsewhere; <see cref="IsRemoved"/> is already true, and a container's inventory no longer takes clicks.
    /// </summary>
    protected internal virtual void OnRemoved()
    {
    }

    /// <summary>
    /// Called once a plugin's block entity is live in its level, like NeoForge's <c>onLoad</c>: after it was placed,
    /// its chunk loaded, or its plugin loaded again, on the level's next tick, whether or not its chunk ticks, and
    /// before its first <see cref="TickAsync"/>. Join what spans several block entities here, such as a network
    /// (see <see cref="ILevel.AddTicker"/>), and leave it in <see cref="OnRemoved"/> and <see cref="OnChunkUnloaded"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="Level"/> is set. One removed or unloaded before that tick gets neither this nor
    /// <see cref="OnChunkUnloaded"/>, but still gets <see cref="OnRemoved"/>. A call that throws is logged.
    /// </remarks>
    protected internal virtual void OnLoad()
    {
    }

    /// <summary>
    /// Called when the block entity's chunk unloads, like NeoForge's <c>onChunkUnloaded</c>: after the chunk's final
    /// save, between two of the level's ticks, holding no lock. The block entity is gone from then on (its block stays
    /// saved, and its chunk loading again creates a new one), so drop what refers to it.
    /// </summary>
    /// <remarks>
    /// <see cref="IsRemoved"/> is already true, a container's inventory no longer takes clicks, and
    /// <see cref="Level"/> is still set for this call. Don't change the world from it: its chunk is saved. Not called
    /// when its block is removed (see <see cref="OnRemoved"/>), its plugin unloads, or the server stops (which saves
    /// its chunk without unloading it). A call that throws is logged.
    /// </remarks>
    protected internal virtual void OnChunkUnloaded()
    {
    }

    /// <summary>
    /// Tells the server the block entity's saved data changed, so that the next save has it. The server takes the
    /// change in at the end of the level's tick, calling <see cref="Save"/>.
    /// </summary>
    public void SetChanged()
    {
        if (Interlocked.Exchange(ref this.changed, 1) == 0)
            this.Host?.OnChanged(this);
    }

    /// <summary>
    /// Sends what <see cref="GetUpdateData"/> returns at the end of the level's tick to the players who have the block
    /// entity's chunk, like vanilla's block entity data packet; nothing when it returns null. Calls in one tick send
    /// one update.
    /// </summary>
    public void SendUpdate() => this.Host?.SendUpdate(this);

    /// <summary>
    /// Takes in a change <see cref="SetChanged"/> announced.
    /// </summary>
    /// <returns>Whether there was one.</returns>
    internal bool TakeChange() => Interlocked.Exchange(ref this.changed, 0) == 1;

    public override string ToString() => $"{this.Id} at {this.BlockPosition}";
}

/// <summary>
/// What a block entity reaches of its level: the server implements it for each level.
/// </summary>
internal abstract class BlockEntityHost
{
    /// <summary>
    /// The level.
    /// </summary>
    public abstract ILevel Level { get; }

    /// <summary>
    /// The server <paramref name="blockEntity"/>'s plugin was given, or null for the server's own block entities.
    /// </summary>
    public abstract IServer? ServerOf(BlockEntity blockEntity);

    /// <summary>
    /// Queues a block entity whose data changed, so that the level takes the change in at the end of its tick.
    /// </summary>
    public abstract void OnChanged(BlockEntity blockEntity);

    /// <summary>
    /// Queues a block entity's update, which the level sends to the players who have its chunk at the end of its tick.
    /// </summary>
    public abstract void SendUpdate(BlockEntity blockEntity);
}
