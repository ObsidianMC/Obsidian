using Obsidian.API.ItemComponents;

namespace Obsidian.Registries;
public static class ItemComponentsRegistry
{
    private static readonly int MaxComponents = Enum.GetValues<ItemComponentType>().Length;
    public static IReadOnlyList<Func<IItemComponent>> Components => new List<Func<IItemComponent>>(MaxComponents)
    {
        () => new CustomDataItemComponent(),
        () => new MaxStackSizeItemComponent(),
        () => new MaxDamageItemComponent(),
        () => new DamageItemComponent(),
        () => default!,
        () => new CustomNameItemComponent(),
        () => new ItemNameItemComponent()

    }.AsReadOnly();
}
