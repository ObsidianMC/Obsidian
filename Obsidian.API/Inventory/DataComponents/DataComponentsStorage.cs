using System.Collections;

namespace Obsidian.API.Inventory.DataComponents;
public abstract record class DataComponentsStorage : IEnumerable<IDataComponent>
{
    private readonly Dictionary<DataComponentType, IDataComponent> internalStorage = [];
    private readonly Dictionary<DataComponentType, int> hashedStorage = [];

    public List<DataComponentType> RemoveComponents { get; } = [];

    public int TotalComponents => this.internalStorage.Count;

    public IDataComponent this[DataComponentType type] { get => this.internalStorage[type]; set => this.internalStorage[type] = value; }

    public bool Add(IDataComponent component) => this.internalStorage.TryAdd(component.Type, component);

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
    public bool AddHashedComponent(DataComponentType type, int hash) => this.hashedStorage.TryAdd(type, hash);

    public bool CompareComponentHash(DataComponentType type, int hash) =>
        this.hashedStorage.TryGetValue(type, out var value) && value == hash;

    public bool Remove(DataComponentType type) => this.internalStorage.Remove(type);

    public TComponent? GetComponent<TComponent>(DataComponentType type) where TComponent : IDataComponent =>
        (TComponent)this.internalStorage.GetValueOrDefault(type);

    public bool TryGetComponent<TComponent>(DataComponentType componentType, out IDataComponent component) where TComponent : IDataComponent =>
        this.internalStorage.TryGetValue(componentType, out component);

    public bool TryGetComponent(DataComponentType componentType, out IDataComponent component) =>
       this.internalStorage.TryGetValue(componentType, out component);

    public IEnumerator<IDataComponent> GetEnumerator() => this.internalStorage.Values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}
