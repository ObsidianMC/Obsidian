namespace Obsidian.Net;

/// <summary>
/// One registry's numeric ids as a connection numbers them (the remote ids), translated to and from this process's
/// (the local ids), like NeoForge's client once it applied a server's registry snapshot: an entry keeps its name and
/// takes the server's id.
/// </summary>
/// <remarks>
/// A connection numbering the registry like this process gets an identity map, whose lookups pass ids through.
/// </remarks>
internal sealed class RegistryIdMap
{
    // Indexed by remote id, the local id or -1; and indexed by local id, the remote id or -1. Null for the identity.
    private readonly int[]? toLocal;
    private readonly int[]? toRemote;

    private RegistryIdMap(int remoteCount, int[]? toLocal, int[]? toRemote)
    {
        this.RemoteCount = remoteCount;
        this.toLocal = toLocal;
        this.toRemote = toRemote;
    }

    /// <summary>
    /// The identity map of a registry whose size on the other side isn't known.
    /// </summary>
    public static RegistryIdMap Identity { get; } = new(-1, null, null);

    /// <summary>
    /// How many ids the connection's registry has (its highest id and one), or -1 if that isn't known.
    /// </summary>
    public int RemoteCount { get; }

    /// <summary>
    /// Whether the connection numbers the registry's entries like this process.
    /// </summary>
    public bool IsIdentity => this.toLocal is null;

    /// <summary>
    /// The identity map of a registry the connection has <paramref name="count"/> entries of, numbered like the first
    /// <paramref name="count"/> of this process's.
    /// </summary>
    public static RegistryIdMap IdentityOf(int count) => new(count, null, null);

    /// <summary>
    /// Builds the map of a registry the connection has <c>localIds.Count</c> ids of.
    /// </summary>
    /// <param name="localIds">
    /// Indexed by remote id, the local id of the same entry, or -1 if this process lacks it.
    /// </param>
    /// <param name="localCount">How many ids this process's registry has.</param>
    /// <returns>An identity map if every remote id is its local id.</returns>
    public static RegistryIdMap Create(IReadOnlyList<int> localIds, int localCount)
    {
        var identity = true;
        for (var remote = 0; remote < localIds.Count && identity; remote++)
            identity = localIds[remote] == remote;

        if (identity)
            return IdentityOf(localIds.Count);

        var toLocal = new int[localIds.Count];
        var toRemote = new int[localCount];
        Array.Fill(toRemote, -1);

        for (var remote = 0; remote < toLocal.Length; remote++)
        {
            var local = localIds[remote];
            toLocal[remote] = local;

            if ((uint)local < (uint)toRemote.Length)
                toRemote[local] = remote;
        }

        return new RegistryIdMap(localIds.Count, toLocal, toRemote);
    }

    /// <summary>
    /// The local id of the connection's <paramref name="remoteId"/>, or -1 if this process lacks the entry. The
    /// identity map passes every id through, so callers check ranges like for ids they didn't translate.
    /// </summary>
    public int ToLocal(int remoteId)
    {
        if (this.toLocal is null)
            return remoteId;

        return (uint)remoteId < (uint)this.toLocal.Length ? this.toLocal[remoteId] : -1;
    }

    /// <summary>
    /// The connection's id of the local <paramref name="localId"/>, or -1 if the connection lacks the entry.
    /// </summary>
    public int ToRemote(int localId)
    {
        if (this.toRemote is null)
            return this.RemoteCount < 0 || localId < this.RemoteCount ? localId : -1;

        return (uint)localId < (uint)this.toRemote.Length ? this.toRemote[localId] : -1;
    }
}

/// <summary>
/// The numbering of the synced registries a connection reads and writes ids of, where it differs from this process's.
/// Only the Obsidian client sets it, from NeoForge's registry sync; a server's buffers have none.
/// </summary>
/// <remarks>
/// <see cref="NetworkBuffer"/> translates the ids in item stacks with it: items, data component types and the registry
/// ids components hold (<see cref="INetStreamReader.ReadRegistryId"/>). The client's own decoders read the rest
/// (<see cref="Get"/>) and block states (<see cref="BlockStates"/>), which NeoForge numbers from the blocks' order
/// rather than syncing them.
/// </remarks>
internal sealed class RegistryIdMaps
{
    public const string Blocks = "minecraft:block";
    public const string Items = "minecraft:item";
    public const string DataComponentTypes = "minecraft:data_component_type";

    private readonly IReadOnlyDictionary<string, RegistryIdMap> maps;

    /// <param name="maps">The registries' maps by name; registries without one keep this process's numbering.</param>
    /// <param name="blockStates">The block states' map, numbered from the connection's block order.</param>
    public RegistryIdMaps(IReadOnlyDictionary<string, RegistryIdMap> maps, RegistryIdMap blockStates)
    {
        this.maps = maps;
        this.BlockStates = blockStates;
        this.Item = this.Get(Items);
        this.DataComponentType = this.Get(DataComponentTypes);
    }

    /// <summary>
    /// The block states, numbered like vanilla from the blocks in id order, each block's states in its own order.
    /// </summary>
    public RegistryIdMap BlockStates { get; }

    public RegistryIdMap Item { get; }

    public RegistryIdMap DataComponentType { get; }

    /// <summary>
    /// The map of the registry named <paramref name="registry"/>, or the identity map if it keeps this process's ids.
    /// </summary>
    public RegistryIdMap Get(string registry) => this.maps.GetValueOrDefault(registry, RegistryIdMap.Identity);
}

/// <summary>
/// A registry entry that one side of a connection lacks, met translating its id with <see cref="RegistryIdMaps"/>.
/// </summary>
internal sealed class MissingRegistryEntryException(string registry, int id)
    : InvalidOperationException($"Entry {id} of {registry} has no counterpart across the connection.");
