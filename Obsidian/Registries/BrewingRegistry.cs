using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Plugins;
using Obsidian.Plugins;
using System.Threading;

namespace Obsidian.Registries;

/// <summary>
/// What brewing stands brew: vanilla's mixes, then the ones plugins added, like vanilla's <c>PotionBrewing</c>. A
/// potion mix turns a bottle of one potion into the same kind of bottle of another; a container mix turns one kind of
/// bottle into another (a potion into a splash potion), keeping its potion.
/// </summary>
/// <remarks>
/// Plugins add mixes with <see cref="IPluginRegistry.AddPotionMix"/> and <see cref="IPluginRegistry.AddContainerMix"/>,
/// and they're removed when the plugin unloads. Lookups read one immutable table, which adding or removing replaces.
/// </remarks>
internal static class BrewingRegistry
{
    /// <summary>
    /// How long a brew takes, in ticks (vanilla's <c>BrewingStandBlockEntity</c>).
    /// </summary>
    public const int BrewTime = 400;

    /// <summary>
    /// How many brews a piece of fuel lasts (vanilla's <c>BrewingStandBlockEntity</c>).
    /// </summary>
    public const int FuelUses = 20;

    private static readonly Lock changing = new();

    // Vanilla's bottles a container mix can start from or make (PotionBrewing.addVanillaMixes's addContainer).
    private static readonly ImmutableArray<int> containers =
    [
        ItemIds.Potion,
        ItemIds.SplashPotion,
        ItemIds.LingeringPotion
    ];

    private static readonly ImmutableArray<ContainerMix> vanillaContainerMixes =
    [
        new(ItemsRegistry.Potion, ItemsRegistry.Gunpowder, ItemsRegistry.SplashPotion),
        new(ItemsRegistry.SplashPotion, ItemsRegistry.DragonBreath, ItemsRegistry.LingeringPotion)
    ];

    private static readonly ImmutableArray<PotionMix> vanillaPotionMixes = VanillaPotionMixes();

    // The plugins' mixes in the order they were added, with the plugin that added each.
    private static ImmutableArray<(IPluginContainer Owner, PotionMix Mix)> pluginPotionMixes = [];
    private static ImmutableArray<(IPluginContainer Owner, ContainerMix Mix)> pluginContainerMixes = [];

    private static MixTable table = new(vanillaPotionMixes, vanillaContainerMixes);

    /// <summary>
    /// Every potion mix, vanilla's first, in the order they're tried.
    /// </summary>
    public static ImmutableArray<PotionMix> PotionMixes => Volatile.Read(ref table).Potions;

    /// <summary>
    /// Every container mix, vanilla's first, in the order they're tried.
    /// </summary>
    public static ImmutableArray<ContainerMix> ContainerMixes => Volatile.Read(ref table).Containers;

    /// <summary>
    /// Whether <paramref name="item"/> is a bottle brewing stands brew: a potion, splash potion or lingering potion.
    /// </summary>
    public static bool IsContainer(ItemStack item) => containers.Contains(item.Holder.Id);

    /// <summary>
    /// Whether <paramref name="ingredient"/> is some mix's ingredient, which the stand's ingredient slot takes
    /// (vanilla's <c>PotionBrewing.isIngredient</c>).
    /// </summary>
    public static bool IsIngredient(ItemStack ingredient)
    {
        var current = Volatile.Read(ref table);

        return current.Containers.Any(mix => mix.Ingredient == ingredient.Holder)
            || current.Potions.Any(mix => mix.Ingredient == ingredient.Holder);
    }

    /// <summary>
    /// Whether <paramref name="ingredient"/> brews <paramref name="bottle"/> into something (vanilla's
    /// <c>PotionBrewing.hasMix</c>).
    /// </summary>
    public static bool HasMix(ItemStack bottle, ItemStack ingredient)
    {
        if (!IsContainer(bottle))
            return false;

        var current = Volatile.Read(ref table);
        if (current.Containers.Any(mix => mix.From == bottle.Holder && mix.Ingredient == ingredient.Holder))
            return true;

        return PotionOf(bottle) is Potion potion
            && current.Potions.Any(mix => mix.From == potion && mix.Ingredient == ingredient.Holder);
    }

    /// <summary>
    /// What <paramref name="ingredient"/> brews <paramref name="bottle"/> into, or the bottle itself (vanilla's
    /// <c>PotionBrewing.mix</c>).
    /// </summary>
    /// <remarks>
    /// Like vanilla's, container mixes are tried before potion mixes, the first that matches wins, and the result is a
    /// new bottle with only its potion: a custom colour, effects or name don't carry over.
    /// </remarks>
    public static ItemStack Mix(ItemStack ingredient, ItemStack bottle)
    {
        if (bottle.IsAir || PotionOf(bottle) is not Potion potion)
            return bottle;

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

        return bottle;
    }

    /// <summary>
    /// Adds <paramref name="owner"/>'s potion mix, after vanilla's and the mixes added before it.
    /// </summary>
    /// <exception cref="InvalidOperationException">The plugin is unloading.</exception>
    public static void Add(IPluginContainer owner, PotionMix mix, Func<bool> closing)
    {
        lock (changing)
        {
            if (closing())
                throw new InvalidOperationException("The plugin is unloading.");

            pluginPotionMixes = pluginPotionMixes.Add((owner, mix));
            Publish();
        }
    }

    /// <summary>
    /// Adds <paramref name="owner"/>'s container mix, after vanilla's and the mixes added before it.
    /// </summary>
    /// <exception cref="ArgumentException">A bottle isn't one brewing stands brew (see <see cref="IsContainer"/>).
    /// </exception>
    /// <exception cref="InvalidOperationException">The plugin is unloading.</exception>
    public static void Add(IPluginContainer owner, ContainerMix mix, Func<bool> closing)
    {
        // Vanilla's builder only takes its containers, which are all its potion slots take.
        if (!containers.Contains(mix.From.Id) || !containers.Contains(mix.To.Id))
            throw new ArgumentException("A container mix turns a potion, splash or lingering potion into another.");

        lock (changing)
        {
            if (closing())
                throw new InvalidOperationException("The plugin is unloading.");

            pluginContainerMixes = pluginContainerMixes.Add((owner, mix));
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
            pluginPotionMixes = pluginPotionMixes.RemoveAll(entry => entry.Owner == owner);
            pluginContainerMixes = pluginContainerMixes.RemoveAll(entry => entry.Owner == owner);
            Publish();
        }
    }

    private static void Publish()
    {
        var potions = vanillaPotionMixes.AddRange(pluginPotionMixes.Select(entry => entry.Mix));
        var bottles = vanillaContainerMixes.AddRange(pluginContainerMixes.Select(entry => entry.Mix));

        Volatile.Write(ref table, new MixTable(potions, bottles));
    }

    private static Potion? PotionOf(ItemStack bottle) =>
        bottle.GetComponent<PotionContentsDataComponent>(DataComponentType.PotionContents)?.Potion;

    // Vanilla's PotionContents.createItemStack: a new bottle with nothing but its potion.
    private static ItemStack Bottle(Item item, Potion potion) =>
        new(item, 1, new PotionContentsDataComponent { Potion = potion });

    // Vanilla's potion mixes in the order PotionBrewing.addVanillaMixes adds them, which decides which mix wins when
    // two match (none of vanilla's overlap) and is the order plugins' mixes come after. A start mix brews water into
    // mundane and awkward into its potion, like vanilla's addStartMix.
    private static ImmutableArray<PotionMix> VanillaPotionMixes()
    {
        var mixes = ImmutableArray.CreateBuilder<PotionMix>();

        void Add(Potion from, Item ingredient, Potion to) => mixes.Add(new PotionMix(from, ingredient, to));

        void Start(Item ingredient, Potion to)
        {
            Add(Potion.Water, ingredient, Potion.Mundane);
            Add(Potion.Awkward, ingredient, to);
        }

        Add(Potion.Water, ItemsRegistry.GlowstoneDust, Potion.Thick);
        Add(Potion.Water, ItemsRegistry.Redstone, Potion.Mundane);
        Add(Potion.Water, ItemsRegistry.NetherWart, Potion.Awkward);
        Start(ItemsRegistry.BreezeRod, Potion.WindCharged);
        Start(ItemsRegistry.SlimeBlock, Potion.Oozing);
        Start(ItemsRegistry.Stone, Potion.Infested);
        Start(ItemsRegistry.Cobweb, Potion.Weaving);
        Add(Potion.Awkward, ItemsRegistry.GoldenCarrot, Potion.NightVision);
        Add(Potion.NightVision, ItemsRegistry.Redstone, Potion.LongNightVision);
        Add(Potion.NightVision, ItemsRegistry.FermentedSpiderEye, Potion.Invisibility);
        Add(Potion.LongNightVision, ItemsRegistry.FermentedSpiderEye, Potion.LongInvisibility);
        Add(Potion.Invisibility, ItemsRegistry.Redstone, Potion.LongInvisibility);
        Start(ItemsRegistry.MagmaCream, Potion.FireResistance);
        Add(Potion.FireResistance, ItemsRegistry.Redstone, Potion.LongFireResistance);
        Start(ItemsRegistry.RabbitFoot, Potion.Leaping);
        Add(Potion.Leaping, ItemsRegistry.Redstone, Potion.LongLeaping);
        Add(Potion.Leaping, ItemsRegistry.GlowstoneDust, Potion.StrongLeaping);
        Add(Potion.Leaping, ItemsRegistry.FermentedSpiderEye, Potion.Slowness);
        Add(Potion.LongLeaping, ItemsRegistry.FermentedSpiderEye, Potion.LongSlowness);
        Add(Potion.Slowness, ItemsRegistry.Redstone, Potion.LongSlowness);
        Add(Potion.Slowness, ItemsRegistry.GlowstoneDust, Potion.StrongSlowness);
        Add(Potion.Awkward, ItemsRegistry.TurtleHelmet, Potion.TurtleMaster);
        Add(Potion.TurtleMaster, ItemsRegistry.Redstone, Potion.LongTurtleMaster);
        Add(Potion.TurtleMaster, ItemsRegistry.GlowstoneDust, Potion.StrongTurtleMaster);
        Add(Potion.Swiftness, ItemsRegistry.FermentedSpiderEye, Potion.Slowness);
        Add(Potion.LongSwiftness, ItemsRegistry.FermentedSpiderEye, Potion.LongSlowness);
        Start(ItemsRegistry.Sugar, Potion.Swiftness);
        Add(Potion.Swiftness, ItemsRegistry.Redstone, Potion.LongSwiftness);
        Add(Potion.Swiftness, ItemsRegistry.GlowstoneDust, Potion.StrongSwiftness);
        Add(Potion.Awkward, ItemsRegistry.Pufferfish, Potion.WaterBreathing);
        Add(Potion.WaterBreathing, ItemsRegistry.Redstone, Potion.LongWaterBreathing);
        Start(ItemsRegistry.GlisteringMelonSlice, Potion.Healing);
        Add(Potion.Healing, ItemsRegistry.GlowstoneDust, Potion.StrongHealing);
        Add(Potion.Healing, ItemsRegistry.FermentedSpiderEye, Potion.Harming);
        Add(Potion.StrongHealing, ItemsRegistry.FermentedSpiderEye, Potion.StrongHarming);
        Add(Potion.Harming, ItemsRegistry.GlowstoneDust, Potion.StrongHarming);
        Add(Potion.Poison, ItemsRegistry.FermentedSpiderEye, Potion.Harming);
        Add(Potion.LongPoison, ItemsRegistry.FermentedSpiderEye, Potion.Harming);
        Add(Potion.StrongPoison, ItemsRegistry.FermentedSpiderEye, Potion.StrongHarming);
        Start(ItemsRegistry.SpiderEye, Potion.Poison);
        Add(Potion.Poison, ItemsRegistry.Redstone, Potion.LongPoison);
        Add(Potion.Poison, ItemsRegistry.GlowstoneDust, Potion.StrongPoison);
        Start(ItemsRegistry.GhastTear, Potion.Regeneration);
        Add(Potion.Regeneration, ItemsRegistry.Redstone, Potion.LongRegeneration);
        Add(Potion.Regeneration, ItemsRegistry.GlowstoneDust, Potion.StrongRegeneration);
        Start(ItemsRegistry.BlazePowder, Potion.Strength);
        Add(Potion.Strength, ItemsRegistry.Redstone, Potion.LongStrength);
        Add(Potion.Strength, ItemsRegistry.GlowstoneDust, Potion.StrongStrength);
        Add(Potion.Water, ItemsRegistry.FermentedSpiderEye, Potion.Weakness);
        Add(Potion.Weakness, ItemsRegistry.Redstone, Potion.LongWeakness);
        Add(Potion.Awkward, ItemsRegistry.PhantomMembrane, Potion.SlowFalling);
        Add(Potion.SlowFalling, ItemsRegistry.Redstone, Potion.LongSlowFalling);

        return mixes.ToImmutable();
    }

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
