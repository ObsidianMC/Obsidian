using Obsidian.API.BlockEntities;
using Obsidian.Nbt;
using System.Collections.Generic;
using Obsidian.API.Data;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API.Client;

/// <summary>
/// Read-only access to the dimension the client is viewing. It contains only what the server sent, including local
/// block prediction; queries never load chunks. Obtain it from <see cref="IClientConnection.World"/> or
/// <see cref="ClientBlockEntityExtensions.GetClientWorld"/>.
/// </summary>
/// <remarks>
/// Block, chunk and synced block entity data queries may run on any thread. Each observes the state at that call;
/// several calls are not an atomic world snapshot. Entity queries and live block entity access require the game's
/// thread (ticks, key bindings and screens), and throw <see cref="System.InvalidOperationException"/> elsewhere.
/// Do not retain the view across disconnects; obtain the current connection's view again.
/// </remarks>
public interface IClientWorld
{
    /// <summary>Reads a detached synced attachment of the local player. Game thread only.</summary>
    public bool TryGetPlayerAttachment<TValue>(AttachmentType<TValue> type, [MaybeNullWhen(false)] out TValue value);

    /// <summary>Reads a detached synced attachment of a tracked entity. Game thread only.</summary>
    public bool TryGetEntityAttachment<TValue>(
        int entityId, AttachmentType<TValue> type, [MaybeNullWhen(false)] out TValue value);

    /// <summary>Reads a detached synced attachment of a loaded chunk column. Game thread only.</summary>
    public bool TryGetChunkAttachment<TValue>(
        int chunkX, int chunkZ, AttachmentType<TValue> type, [MaybeNullWhen(false)] out TValue value);

    /// <summary>
    /// Whether the client has a chunk column, using chunk coordinates (block coordinates shifted by 4).
    /// </summary>
    public bool IsChunkLoaded(int chunkX, int chunkZ);

    /// <summary>
    /// The block state at the position, or air for an unloaded chunk or outside the dimension's height.
    /// </summary>
    public IBlock GetBlock(Vector position);

    /// <summary>
    /// A deep copy of the block entity's synced NBT, or null when none is present. Changing the result cannot change
    /// the client's published data. This is update data, not the server's complete saved data.
    /// </summary>
    public NbtCompound? GetBlockEntityData(Vector position);

    /// <summary>
    /// The live client instance of a mod's block entity, or null before its first tick or after removal. Game thread
    /// only: use your mod's class and never access or retain this mutable instance from another thread. A packet
    /// update is applied to it on the next client tick; synced NBT may already be newer.
    /// </summary>
    public BlockEntity? GetBlockEntity(Vector position);

    /// <summary>A detached snapshot of a tracked entity, or null if absent or removed. Game thread only.</summary>
    public IClientEntity? GetEntity(int id);

    /// <summary>
    /// Detached snapshots of entities whose current positions are inside the box, including its boundary.
    /// Tests positions, not collision boxes. Game thread only; result order is unspecified.
    /// </summary>
    public IReadOnlyList<IClientEntity> GetEntities(BoundingBox bounds);
}

/// <summary>
/// One detached entity snapshot. Its properties and metadata table are read-only. Metadata values keep the client's
/// decoded CLR types; mutable values (arrays, NBT dictionaries/lists and item stacks) are deep copies owned by the
/// caller. Retaining or changing them affects neither the client nor subsequent queries.
/// </summary>
public interface IClientEntity
{
    public int Id { get; }

    public Guid Uuid { get; }

    /// <summary>The client's entity type id, including registered plugin types.</summary>
    public EntityType Type { get; }

    /// <summary>The current displayed position, after any interpolation applied so far.</summary>
    public VectorD Position { get; }

    /// <summary>
    /// Synced metadata by protocol index. Missing entries have not been received; null is a valid value.
    /// </summary>
    public IReadOnlyDictionary<byte, object?> Metadata { get; }
}
