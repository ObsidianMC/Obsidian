using System.Threading;

namespace Obsidian.API.Inventory.DataComponents;

/// <summary>
/// A set of components keyed by type, at most one per type: an item stack's. Holds placeholders for the item's
/// defaults, the components set on it (<see cref="Patch"/>) and the defaults it removed
/// (<see cref="RemoveComponents"/>).
/// </summary>
public abstract class DataComponentsStorage
{
    /// <summary>
    /// Gets the components held, placeholders for the item's defaults included.
    /// </summary>
    protected Dictionary<DataComponentType, DataComponent> InternalStorage { get; } = [];
    private readonly List<DataComponentType> patchOrder = [];
    /// <summary>
    /// Gets component hashes a client sent in place of values, by type (see <see cref="AddHashedComponent"/>).
    /// </summary>
    protected Dictionary<DataComponentType, int> HashedStorage { get; } = [];

    /// <summary>
    /// Components that only stand in for the item's own defaults (see <see cref="ComponentBuilder.DefaultItemComponents"/>),
    /// as they were when added. Immutable placeholders aren't part of <see cref="Patch"/>; setting or removing a
    /// component of the type drops its placeholder.
    /// </summary>
    protected Dictionary<DataComponentType, DataComponent> Placeholders { get; } = [];

    /// <summary>
    /// Gets the vanilla component types removed from the item's defaults, sent and saved as removals. Changing the list
    /// directly doesn't count as a change to the storage.
    /// </summary>
    public List<DataComponentType> RemoveComponents { get; } = [];

    /// <summary>
    /// Gets the number of components held, placeholders included.
    /// </summary>
    public int TotalComponents => this.InternalStorage.Count;

    private long version;

    /// <summary>
    /// The <see cref="ChangeStamp"/> of the storage's last change: a component set, added or removed through its
    /// methods, or, for a stack, its count. Component values are immutable: use a <c>with</c> expression and assign
    /// the replacement through the indexer to record a change. Editing <see cref="RemoveComponents"/> directly
    /// doesn't count.
    /// </summary>
    internal long Version => Volatile.Read(ref this.version);

    /// <summary>
    /// The components that were set on this storage, as opposed to placeholders for the item's defaults. Like vanilla's
    /// <c>DataComponentPatch</c>, this is what goes over the network.
    /// </summary>
    public IEnumerable<DataComponent> Patch => this.patchOrder
        .Where(type => this.InternalStorage.ContainsKey(type) && !this.RemoveComponents.Contains(type))
        .Select(type => this.InternalStorage[type])
        .Concat(this.InternalStorage.Values.Where(component => !this.patchOrder.Contains(component.Type)
            && !this.RemoveComponents.Contains(component.Type)
            && (!this.Placeholders.TryGetValue(component.Type, out var placeholder) || !component.Equals(placeholder))));

    /// <summary>
    /// Gets or sets the component of <paramref name="type"/>. Setting one makes it part of <see cref="Patch"/> and
    /// undoes its type's removal.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Getting a type the storage doesn't hold.</exception>
    /// <exception cref="ArgumentException">Setting a component whose <see cref="DataComponent.Type"/> isn't
    /// <paramref name="type"/>.</exception>
    public DataComponent this[DataComponentType type]
    {
        get => this.InternalStorage[type];
        set
        {
            if (value.Type != type)
                throw new ArgumentException("Component type does not match its key.", nameof(value));

            if (!this.patchOrder.Contains(type))
                this.patchOrder.Add(type);

            this.InternalStorage[type] = value;
            this.RemoveComponents.Remove(type);
            this.Placeholders.Remove(type);
            this.MarkChanged();
        }
    }

    /// <summary>
    /// Adds <paramref name="component"/> if the storage holds none of its type, undoing its type's removal.
    /// </summary>
    /// <returns>Whether it was added.</returns>
    public bool Add(DataComponent component)
    {
        if (!this.InternalStorage.TryAdd(component.Type, component))
            return false;

        this.RemoveComponents.Remove(component.Type);
        this.MarkChanged();

        return true;
    }

    /// <summary>
    /// Adds a hashed components to the item.
    /// </summary>
    /// <param name="type">The component type.</param>
    /// <param name="hash">The Crc32 hash of that component</param>
    /// <returns>True if it was added otherwise false.</returns>
    /// <remarks>
    /// The hashes are sent from the client. The server does not generate/send hashed components.
    /// The only time the server will generate these hashes is to compare already existing items. 
    /// i.e to compare if the item the client sent back is the same as the one on the server.
    /// </remarks>
    public bool AddHashedComponent(DataComponentType type, int hash) => this.HashedStorage.TryAdd(type, hash);

    /// <summary>
    /// Whether the client sent <paramref name="hash"/> as the hash of the component of <paramref name="type"/>.
    /// </summary>
    public bool CompareComponentHash(DataComponentType type, int hash) =>
        this.HashedStorage.TryGetValue(type, out var value) && value == hash;

    /// <summary>
    /// Removes the component of <paramref name="type"/>. For a vanilla type the removal is kept in
    /// <see cref="RemoveComponents"/>, so the item's default doesn't come back.
    /// </summary>
    /// <returns>Whether the storage held a component of the type.</returns>
    public bool Remove(DataComponentType type)
    {
        this.Placeholders.Remove(type);
        this.patchOrder.Remove(type);

        // Like vanilla's patch, only a default can be removed; a plugin's type is never an item's default.
        if (Registries.DataComponentTypesRegistry.IsVanilla(type) && !this.RemoveComponents.Contains(type))
            this.RemoveComponents.Add(type);

        this.MarkChanged();

        return this.InternalStorage.Remove(type);
    }

    /// <summary>
    /// Records a change to the storage (see <see cref="Version"/>).
    /// </summary>
    private protected void MarkChanged() => Volatile.Write(ref this.version, ChangeStamp.Next());

    /// <summary>
    /// Gets the component of <paramref name="type"/>, or null when there's none or it isn't a
    /// <typeparamref name="TComponent"/>.
    /// </summary>
    public TComponent? GetComponent<TComponent>(DataComponentType type) where TComponent : DataComponent =>
        this.InternalStorage.GetValueOrDefault(type) as TComponent;

    /// <summary>
    /// Gets the component of <paramref name="componentType"/>. The type argument isn't checked:
    /// <paramref name="component"/> may be of another class.
    /// </summary>
    /// <returns>Whether the storage holds a component of the type.</returns>
    public bool TryGetComponent<TComponent>(
        DataComponentType componentType, out DataComponent component) where TComponent : DataComponent =>
        this.InternalStorage.TryGetValue(componentType, out component);

    /// <summary>
    /// Gets the component of <paramref name="componentType"/>.
    /// </summary>
    /// <returns>Whether the storage holds a component of the type.</returns>
    public bool TryGetComponent(DataComponentType componentType, out DataComponent component) =>
       this.InternalStorage.TryGetValue(componentType, out component);

    /// <summary>
    /// Whether the storage holds a component of <paramref name="type"/>, placeholders included.
    /// </summary>
    public bool ContainsKey(DataComponentType type) => this.InternalStorage.ContainsKey(type);

    /// <summary>
    /// Returns the components held, placeholders included, in no particular order.
    /// </summary>
    public IEnumerator<DataComponent> GetEnumerator() => this.InternalStorage.Values.GetEnumerator();
}
