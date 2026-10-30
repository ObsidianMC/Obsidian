using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Plugins;
using Obsidian.Plugins;
using System.Threading;

namespace Obsidian.Registries;

/// <summary>
/// The potion and container mixes plugins added (<see cref="IPluginRegistry.AddPotionMix"/> and
/// <see cref="IPluginRegistry.AddContainerMix"/>), which brewing stands try after the brewing recipes. A potion mix
/// turns a bottle of one potion into the same kind of bottle of another; a container mix turns one kind of bottle into
/// another (a potion into a splash potion), keeping its potion.
/// </summary>
/// <remarks>
/// A plugin's mixes are removed when it unloads. Lookups read one immutable table, which adding or removing replaces.
/// </remarks>
internal static class PluginPotionMixes
{
    private static readonly Lock changing = new();

    // The bottles a container mix can start from or make, the ones brewing stands brew.
    private static readonly ImmutableArray<int> containers =
    [
        ItemIds.Potion,
        ItemIds.SplashPotion,
        ItemIds.LingeringPotion
    ];

    // The plugins' mixes in the order they were added, with the plugin that added each.
    private static ImmutableArray<(IPluginContainer Owner, PotionMix Mix)> potionMixes = [];
    private static ImmutableArray<(IPluginContainer Owner, ContainerMix Mix)> containerMixes = [];

    private static MixTable table = new([], []);

    /// <summary>
    /// Every potion mix, in the order they're tried.
    /// </summary>
    public static ImmutableArray<PotionMix> PotionMixes => Volatile.Read(ref table).Potions;

    /// <summary>
    /// Whether <paramref name="ingredient"/> is some mix's ingredient, which the stand's ingredient slot takes.
    /// </summary>
    public static bool IsIngredient(ItemStack ingredient)
    {
        var current = Volatile.Read(ref table);

        return current.Containers.Any(mix => mix.Ingredient == ingredient.Holder)
            || current.Potions.Any(mix => mix.Ingredient == ingredient.Holder);
    }

    /// <summary>
    /// What <paramref name="ingredient"/> brews <paramref name="bottle"/> into, or null when no mix does.
    /// </summary>
    /// <remarks>
    /// Container mixes are tried before potion mixes and the first that matches wins. The result is a new bottle with
    /// only its potion, like a brewing recipe's.
    /// </remarks>
    public static ItemStack? Mix(ItemStack bottle, ItemStack ingredient)
    {
        if (!containers.Contains(bottle.Holder.Id) || PotionOf(bottle) is not Potion potion)
            return null;

        var current = Volatile.Read(ref table);
        foreach (var mix in current.Containers)
        {
            if (mix.From == bottle.Holder && mix.Ingredient == ingredient.Holder)
                return Bottle(mix.To, potion);
        }

        foreach (var mix in current.Potions)
        {
            if (mix.From == potion && mix.Ingredient == ingredient.Holder)
                return Bottle(bottle.Holder, mix.To);
        }

        return null;
    }

    /// <summary>
    /// Adds <paramref name="owner"/>'s potion mix, after the mixes added before it.
    /// </summary>
    /// <exception cref="InvalidOperationException">The plugin is unloading.</exception>
    public static void Add(IPluginContainer owner, PotionMix mix, Func<bool> closing)
    {
        lock (changing)
        {
            if (closing())
                throw new InvalidOperationException("The plugin is unloading.");

            potionMixes = potionMixes.Add((owner, mix));
            Publish();
        }
    }

    /// <summary>
    /// Adds <paramref name="owner"/>'s container mix, after the mixes added before it.
    /// </summary>
    /// <exception cref="ArgumentException">A bottle isn't one brewing stands brew.</exception>
    /// <exception cref="InvalidOperationException">The plugin is unloading.</exception>
    public static void Add(IPluginContainer owner, ContainerMix mix, Func<bool> closing)
    {
        if (!containers.Contains(mix.From.Id) || !containers.Contains(mix.To.Id))
            throw new ArgumentException("A container mix turns a potion, splash or lingering potion into another.");

        lock (changing)
        {
            if (closing())
                throw new InvalidOperationException("The plugin is unloading.");

            containerMixes = containerMixes.Add((owner, mix));
            Publish();
        }
    }

    /// <summary>
    /// Removes every mix <paramref name="owner"/> added, as it unloads.
    /// </summary>
    public static void ReleasePlugin(IPluginContainer owner)
    {
        lock (changing)
        {
            potionMixes = potionMixes.RemoveAll(entry => entry.Owner == owner);
            containerMixes = containerMixes.RemoveAll(entry => entry.Owner == owner);
            Publish();
        }
    }

    private static void Publish()
    {
        var potions = potionMixes.Select(entry => entry.Mix).ToImmutableArray();
        var bottles = containerMixes.Select(entry => entry.Mix).ToImmutableArray();

        Volatile.Write(ref table, new MixTable(potions, bottles));
    }

    private static Potion? PotionOf(ItemStack bottle) =>
        bottle.GetComponent<PotionContentsDataComponent>(DataComponentType.PotionContents)?.Potion;

    private static ItemStack Bottle(Item item, Potion potion) =>
        new(item, 1, new PotionContentsDataComponent { Potion = potion });

    private sealed record MixTable(ImmutableArray<PotionMix> Potions, ImmutableArray<ContainerMix> Containers);
}

/// <summary>
/// A potion mix: <see cref="Ingredient"/> brews a bottle of <see cref="From"/> into one of <see cref="To"/>.
/// </summary>
internal sealed record PotionMix(Potion From, Item Ingredient, Potion To);

/// <summary>
/// A container mix: <see cref="Ingredient"/> brews a <see cref="From"/> bottle into a <see cref="To"/> bottle.
/// </summary>
internal sealed record ContainerMix(Item From, Item Ingredient, Item To);
