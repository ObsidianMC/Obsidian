namespace Obsidian.API.Inventory.DataComponents;
public abstract class DataComponentsStorage
{
    protected Dictionary<DataComponentType, DataComponent> InternalStorage { get; } = [];
    protected Dictionary<DataComponentType, int> HashedStorage { get; } = [];

    /// <summary>
    /// Types whose component only stands in for the item's own default (see <see cref="ComponentBuilder.DefaultItemComponents"/>)
    /// and isn't part of <see cref="Patch"/>. Setting or removing a component of the type clears it.
    /// </summary>
    protected HashSet<DataComponentType> PlaceholderTypes { get; } = [];

    public List<DataComponentType> RemoveComponents { get; } = [];

    public int TotalComponents => this.InternalStorage.Count;

    /// <summary>
    /// The components that were set on this storage, as opposed to placeholders for the item's defaults. Like vanilla's
    /// <c>DataComponentPatch</c>, this is what goes over the network.
    /// </summary>
    public IEnumerable<DataComponent> Patch => this.InternalStorage.Values.Where(component => !this.PlaceholderTypes.Contains(component.Type));

    public DataComponent this[DataComponentType type]
    {
        get => this.InternalStorage[type];
        set
        {
            this.InternalStorage[type] = value;
            this.PlaceholderTypes.Remove(type);
        }
    }

    public bool Add(DataComponent component) => this.InternalStorage.TryAdd(component.Type, component);

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

    public bool CompareComponentHash(DataComponentType type, int hash) =>
        this.HashedStorage.TryGetValue(type, out var value) && value == hash;

    public bool Remove(DataComponentType type)
    {
        this.PlaceholderTypes.Remove(type);
        return this.InternalStorage.Remove(type);
    }

    public TComponent? GetComponent<TComponent>(DataComponentType type) where TComponent : DataComponent =>
        (TComponent)this.InternalStorage.GetValueOrDefault(type);

    public bool TryGetComponent<TComponent>(DataComponentType componentType, out DataComponent component) where TComponent : DataComponent =>
        this.InternalStorage.TryGetValue(componentType, out component);

    public bool TryGetComponent(DataComponentType componentType, out DataComponent component) =>
       this.InternalStorage.TryGetValue(componentType, out component);

    public bool ContainsKey(DataComponentType type) => this.InternalStorage.ContainsKey(type);

    public IEnumerator<DataComponent> GetEnumerator() => this.InternalStorage.Values.GetEnumerator();
}
