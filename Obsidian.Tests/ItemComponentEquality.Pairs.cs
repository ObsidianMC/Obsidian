using Obsidian.API;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Registries;
using Obsidian.API.Registry.Codecs.ArmorTrims;
using Obsidian.API.Registry.Codecs.ArmorTrims.TrimMaterial;
using Obsidian.API.Registry.Codecs.ArmorTrims.TrimPattern;
using Obsidian.Nbt;
using Obsidian.Tests.Utilities;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Xunit;
using PlayerEntity = Obsidian.Entities.Player;

namespace Obsidian.Tests;

public sealed partial class ItemComponentEquality
{
    // Two stacks of two, one holding each component, added together: equal components merge into one stack of four,
    // and a component that differs in any one field keeps both stacks, each with its own component.
    [Fact]
    public void SingleFieldDifferencesDecideMerging()
    {
        var pairs = ComponentPairs().Concat(FloatingPointPairs());

        FixtureCases.AssertAll(pairs, pair => pair.Name, AssertMergeOutcome);
    }

    // Equal potions merge only as far as the stack's size allows, and nothing is lost either way.
    [Fact]
    public void StackLimitsCapMergedPotions()
    {
        (Item Item, int MaxStackSize, int Merged)[] cases =
        [
            (ItemsRegistry.Potion, 1, 1),
            (ItemsRegistry.Potion, 16, 2),
            (ItemsRegistry.TippedArrow, 64, 2)
        ];

        FixtureCases.AssertAll(cases, row => $"{row.Item.UnlocalizedName}, max={row.MaxStackSize}", AssertStackLimit);
    }

    private sealed record ComponentPair(string Name, DataComponent First, DataComponent Second, bool Merges);

    private static ComponentPair Merges(string name, DataComponent first, DataComponent second) =>
        new(name, first, second, Merges: true);

    private static ComponentPair Differs(string name, DataComponent first, DataComponent second) =>
        new(name, first, second, Merges: false);

    private static IEnumerable<ComponentPair> ComponentPairs()
    {
        var potion = CreatePotion(1);
        var effect = potion.CustomEffects[0];
        var model = (CustomModelDataComponent)CreateComponent(DataComponentType.CustomModelData, 1);
        var trim = DescribedTrim(material: true, TrimChat());
        var keybind = new ChatMessage { Keybind = "key.jump" };
        var otherKeybind = new ChatMessage { Keybind = "key.sneak" };
        var emptyEffects = new PotionContentsDataComponent { CustomEffects = [] };

        yield return Differs("book keybind", Book(keybind), Book(otherKeybind));
        yield return Differs("attribute display keybind", Attributes(keybind), Attributes(otherKeybind));

        yield return Differs("potion type", potion, potion with { Potion = Potion.Water });
        yield return Differs("potion color", potion, potion with { CustomColor = 42 });
        yield return Differs("potion name", potion, potion with { CustomName = "different" });
        yield return Differs("potion effect", potion,
            potion with { CustomEffects = [effect with { Duration = 42 }, Effect(2)] });
        yield return Differs("potion hidden effect", potion,
            potion with { CustomEffects = [effect with { HiddenEffect = Effect(42) }, Effect(2)] });
        yield return Differs("potion effect order", potion, potion with { CustomEffects = [Effect(2), effect] });
        yield return Differs("potion effect count", potion, potion with { CustomEffects = [effect] });

        yield return Differs("model floats", model, model with { Floats = [2, 3] });
        yield return Differs("model flags", model, model with { Flags = [false, false] });
        yield return Differs("model strings", model, model with { Strings = ["changed", "1"] });
        yield return Differs("model colors", model, model with { Colors = [2, 0x123456] });
        yield return Merges("model float 1 and 1", ModelFloat(1), ModelFloat(1));
        yield return Differs("model float 1 and 2", ModelFloat(1), ModelFloat(2));
        yield return Merges("model float +inf and +inf", ModelFloat(float.PositiveInfinity),
            ModelFloat(float.PositiveInfinity));
        yield return Differs("model float +inf and -inf", ModelFloat(float.PositiveInfinity),
            ModelFloat(float.NegativeInfinity));

        yield return Merges("map decoration order", Decorations(("first", 1), ("second", 2)),
            Decorations(("second", 2), ("first", 1)));

        yield return Merges("empty and empty effects", emptyEffects,
            new PotionContentsDataComponent { CustomEffects = ImmutableArray<PotionEffectData>.Empty });
        yield return Merges("empty and default effects", emptyEffects, emptyEffects with { CustomEffects = default });
        yield return Differs("missing and empty allowed entities", ComponentBuilder.Equippable,
            ComponentBuilder.Equippable with { AllowedEntities = [] });
        yield return Differs("empty can break and can place on", new CanBreakDataComponent(),
            new CanPlaceOnDataComponent());

        foreach (var material in new[] { true, false })
        {
            var part = material ? "material" : "pattern";
            yield return Merges($"{part} trim, separate equal descriptions", DescribedTrim(material, TrimChat()),
                DescribedTrim(material, TrimChat()));
            yield return Differs($"{part} trim description extra", DescribedTrim(material, TrimChat()),
                DescribedTrim(material, TrimChat(extra: "changed")));
            yield return Differs($"{part} trim description argument", DescribedTrim(material, TrimChat()),
                DescribedTrim(material, TrimChat(argument: "changed")));
        }

        yield return Differs("trim material id", trim, trim with { MaterialRegistryId = 1 });
        yield return Differs("trim pattern id", trim, trim with { PatternRegistryId = 1 });
        yield return Differs("trim palette", trim,
            trim with { Material = trim.Material with { PaletteId = "test:changed" } });
        yield return Differs("trim asset", trim,
            trim with { Pattern = trim.Pattern with { AssetId = "test:changed" } });
        yield return Differs("trim decal", trim, trim with { Pattern = trim.Pattern with { Decal = true } });
        yield return Differs("trim tooltip", trim, trim with { ShowInToolTip = true });

        foreach (var type in new[] { DataComponentType.CanBreak, DataComponentType.CanPlaceOn })
        {
            var first = PartialPredicate(1, 10);
            var second = PartialPredicate(2, 20);
            var damage = ComponentBuilder.Damage with { Value = 1 };
            var repairCost = ComponentBuilder.RepairCost with { Value = 2 };

            yield return Merges($"{type} partial predicate order", Partial(type, first, second),
                Partial(type, second, first));
            yield return Differs($"{type} partial predicate type", Partial(type, first),
                Partial(type, PartialPredicate(2, 10)));
            yield return Differs($"{type} partial predicate value", Partial(type, first),
                Partial(type, PartialPredicate(1, 20)));
            yield return Differs($"{type} exact predicate order", Exact(type, damage, repairCost),
                Exact(type, repairCost, damage));
            yield return Merges(
                $"{type} default and empty partial predicates",
                Predicates(type, new BlockPredicate { PartialComponents = default }),
                Predicates(type, new BlockPredicate { PartialComponents = [] }));
        }
    }

    private static void AssertMergeOutcome(ComponentPair pair)
    {
        var stored = new ItemStack(ItemsRegistry.Diamond, 2, pair.First);
        var incoming = new ItemStack(ItemsRegistry.Diamond, 2, pair.Second);
        var inventory = new Container(46);
        inventory.SetItem(36, stored);

        PlayerEntity.AddToInventory(inventory, 36, incoming);

        var first = inventory.GetItem(36)!;
        var second = inventory.GetItem(37);
        Assert.Equal(0, incoming.Count);
        Assert.Same(pair.First, Component(first, pair.First.Type));
        if (pair.Merges)
        {
            Assert.Equal(pair.First, pair.Second);
            Assert.Equal(pair.First.GetHashCode(), pair.Second.GetHashCode());
            Assert.Equal(4, first.Count);
            Assert.Null(second);
            return;
        }

        Assert.NotEqual(pair.First, pair.Second);
        Assert.Equal(2, first.Count);
        Assert.NotNull(second);
        Assert.Equal(2, second.Count);
        Assert.Same(pair.Second, Component(second, pair.Second.Type));
    }

    private static void AssertStackLimit((Item Item, int MaxStackSize, int Merged) row)
    {
        var stackSize = ComponentBuilder.MaxStackSize with { Value = row.MaxStackSize };
        var stored = new ItemStack(row.Item, 1, CreatePotion(1), stackSize);
        var incoming = new ItemStack(row.Item, 1, CreatePotion(1), stackSize);
        var inventory = new Container(46);
        inventory.SetItem(36, stored);
        Assert.Equal(stored, incoming);
        Assert.Equal(stored.GetHashCode(), incoming.GetHashCode());

        PlayerEntity.AddToInventory(inventory, 36, incoming);

        var left = 2 - row.Merged;
        Assert.Equal(0, incoming.Count);
        Assert.Equal(row.Merged, inventory.GetItem(36)!.Count);
        Assert.Equal(left, inventory.GetItem(37)?.Count ?? 0);
    }

    private static DataComponent Component(ItemStack stack, DataComponentType type)
    {
        Assert.True(stack.TryGetComponent(type, out var component), $"The stack lost its {type}.");
        return component;
    }

    private static WrittenBookDataComponent Book(ChatMessage page) => new()
    {
        Title = "Book",
        Author = "Author",
        Pages = [(page, null)]
    };

    private static AttributeModifiersDataComponent Attributes(ChatMessage display) => new()
    {
        Value = [],
        Displays = [(2, display)]
    };

    private static CustomModelDataComponent ModelFloat(float value) => new() { Floats = [value] };

    private static MapDecorationDataComponent Decorations(params (string Key, int Value)[] decorations) => new()
    {
        Decorations = decorations.ToDictionary(decoration => decoration.Key, decoration => Decoration(decoration.Value))
    };

    private static ChatMessage TrimChat(string extra = "same", string argument = "argument")
    {
        return new ChatMessage { Translate = "test:description" }
            .AddExtra(ChatMessage.Simple(extra))
            .AddChatComponent(ChatMessage.Simple(argument));
    }

    private static TrimDataComponent DescribedTrim(bool material, ChatMessage description) => new()
    {
        Material = new TrimMaterialElement
        {
            PaletteId = "test:palette",
            Description = new TrimDescription { Translate = "test:material" }
        },
        Pattern = new TrimPatternElement
        {
            AssetId = "test:pattern",
            Description = new TrimDescription { Translate = "test:pattern" },
            Decal = false
        },
        MaterialDescription = material ? description : null,
        PatternDescription = material ? null : description
    };

    private static ComponentPredicate PartialPredicate(int typeId, int value) => new()
    {
        IsPredicateType = true,
        TypeId = typeId,
        Value = new NbtCompound { new NbtTag<int>("value", value) }
    };

    private static BlockPredicatesDataComponent Partial(DataComponentType type, params ComponentPredicate[] predicates)
    {
        return Predicates(type, new BlockPredicate { PartialComponents = [.. predicates] });
    }

    private static BlockPredicatesDataComponent Exact(DataComponentType type, params DataComponent[] components)
    {
        return Predicates(type, new BlockPredicate { ExactComponents = [.. components] });
    }

    private static BlockPredicatesDataComponent Predicates(DataComponentType type, BlockPredicate predicate)
    {
        if (type == DataComponentType.CanBreak)
            return new CanBreakDataComponent { Predicates = [predicate] };

        return new CanPlaceOnDataComponent { Predicates = [predicate] };
    }
}
