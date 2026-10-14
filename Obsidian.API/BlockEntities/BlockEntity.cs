using Obsidian.API.Plugins;
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
/// The server saves what <see cref="Save"/> writes as of the last time the block entity called
/// <see cref="SetChanged"/>, taken at the end of the level's tick: call it whenever its saved data changes, like
/// vanilla's <c>setChanged</c>. The server calls <see cref="Load"/>, <see cref="Save"/>, <see cref="TickAsync"/>,
/// <see cref="GetUpdateData"/> and <see cref="OnRemoved"/> on the level's tick, one at a time, besides creating a new
/// one for a block set elsewhere; so code that changes it on the level's tick needs no locking. Chunks are saved and
/// sent from copies of what those calls returned.
/// </para>
/// </remarks>
public abstract class BlockEntity : IBlockEntity
{
    // 1 from SetChanged until the server took the change in (see TakeChange).
    private int changed;

    /// <inheritdoc/>
    public string Id { get; internal set; } = string.Empty;

    /// <inheritdoc/>
    public Vector BlockPosition { get; internal set; }

    /// <summary>
    /// The level the block entity is in, or null while it's in a chunk that isn't part of a level yet (being
    /// generated), or it was created but not placed.
    /// </summary>
    public ILevel? Level => this.Host?.Level;

    /// <summary>
    /// Whether the block entity was removed from its level: its block changed to one without its type, or its
    /// plugin unloaded. A removed block entity isn't ticked, saved or sent again.
    /// </summary>
    public bool IsRemoved { get; internal set; }

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
    /// Reads the block entity's saved data, as <see cref="Save"/> wrote it, when its chunk loads, or when a structure
    /// places it with data. Fields missing from <paramref name="tag"/> mean their defaults.
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
    /// The data clients get for the block entity (vanilla's <c>getUpdateTag</c>), with the chunk and from
    /// <see cref="SendUpdate"/>; null, the default, sends none.
    /// </summary>
    /// <remarks>
    /// A client mod's block entity of the same type reads it, so send only what clients show.
    /// </remarks>
    protected internal virtual NbtCompound? GetUpdateData() => null;

    /// <summary>
    /// Called when the block entity's block is removed from the level, before what a container holds drops, like
    /// vanilla's <c>preRemoveSideEffects</c>: drop what it holds, say. Not called when its chunk unloads, or its plugin
    /// does. Runs on the level's tick after the removal, which may come from elsewhere; <see cref="IsRemoved"/> is
    /// already true, and a container's inventory no longer takes clicks.
    /// </summary>
    protected internal virtual void OnRemoved()
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
    /// Queues a block entity whose data changed, so that the level takes the change in at the end of its tick.
    /// </summary>
    public abstract void OnChanged(BlockEntity blockEntity);

    /// <summary>
    /// Queues a block entity's update, which the level sends to the players who have its chunk at the end of its tick.
    /// </summary>
    public abstract void SendUpdate(BlockEntity blockEntity);
}
