using System.Collections;

namespace Obsidian.API.Inventory.DataComponents;
public abstract class DataComponentsStorage : IEnumerable<IDataComponent>
{
    private readonly Dictionary<DataComponentType, IDataComponent> internalStorage = [];
    public List<DataComponentType> RemoveComponents { get; } = [];

    public int TotalComponents => this.internalStorage.Count;

    public bool Add(IDataComponent component) => this.internalStorage.TryAdd(component.Type, component);
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
