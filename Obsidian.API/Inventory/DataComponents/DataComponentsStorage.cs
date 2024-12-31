using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API.Inventory.DataComponents;
public abstract class DataComponentsStorage : IEnumerable<IDataComponent>
{
    private readonly List<IDataComponent?> internalStorage = new(66);
    public List<DataComponentType> RemoveComponents { get; } = [];

    public int TotalComponents => this.internalStorage.Count;

    public void Add(IDataComponent component) => this.internalStorage[component.GetHashCode()] = component;
    public void Remove(DataComponentType type) => this.internalStorage.RemoveAt(type.GetHashCode());

    public TComponent? GetComponent<TComponent>(DataComponentType type) where TComponent : IDataComponent =>
        (TComponent)this.internalStorage[type.GetHashCode()];

    public IDataComponent? GetComponent(DataComponentType type) => this.internalStorage[type.GetHashCode()];

    public bool TryGetComponent<TComponent>(DataComponentType componentType, [MaybeNullWhen(false)] out IDataComponent? component) where TComponent : IDataComponent
    {
        component = this.GetComponent<TComponent>(componentType);

        return component == null;
    }

    public bool TryGetComponent(DataComponentType componentType, [MaybeNullWhen(false)] out IDataComponent? component)
    {
        component = this.GetComponent(componentType);

        return component == null;
    }

    public IEnumerator<IDataComponent> GetEnumerator() => this.internalStorage.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}
