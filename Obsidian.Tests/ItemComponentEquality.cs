using Obsidian.API;
using Obsidian.API.Effects;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Registries;
using Obsidian.Nbt;
using Obsidian.Net;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Xunit;
using PlayerEntity = Obsidian.Entities.Player;

namespace Obsidian.Tests;

public sealed class ItemComponentEquality
{
    public static TheoryData<DataComponentType> CollectionComponents => new()
    {
        DataComponentType.PotionContents,
        DataComponentType.CustomModelData,
        DataComponentType.Fireworks,
        DataComponentType.Tool,
        DataComponentType.Equippable,
        DataComponentType.Profile,
        DataComponentType.WrittenBookContent,
        DataComponentType.Bees,
        DataComponentType.Consumable,
        DataComponentType.CanPlaceOn,
        DataComponentType.CanBreak,
        DataComponentType.AttributeModifiers,
        DataComponentType.BannerPatterns,
        DataComponentType.MapDecorations,
        DataComponentType.CustomData,
        DataComponentType.BucketEntityData,
        DataComponentType.EntityData,
        DataComponentType.DeathProtection
    };

    [Theory]
    [MemberData(nameof(CollectionComponents))]
    public void EqualComponentsHaveEqualHashesAndMerge(DataComponentType type)
    {
        var first = CreateComponent(type, 1);
        var second = CreateComponent(type, 1);
        var stored = new ItemStack(ItemsRegistry.Diamond, 2, first);
        var incoming = new ItemStack(ItemsRegistry.Diamond, 2, second);
        var inventory = new Container(46);
        inventory.SetItem(36, stored);
        var lookup = new Dictionary<DataComponent, string> { [first] = "found" };

        var equal = first == second;
        var changed = PlayerEntity.AddToInventory(inventory, 36, incoming);

        Assert.NotSame(first, second);
        Assert.True(equal);
        Assert.True(first.Equals((object)second));
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Equal("found", lookup[second]);
        Assert.True(stored == incoming);
        Assert.Equal(stored.GetHashCode(), incoming.GetHashCode());
        Assert.Equal([36], changed);
        Assert.Equal(4, inventory.GetItem(36)!.Count);
        Assert.Equal(0, incoming.Count);
        Assert.Null(inventory.GetItem(37));
    }

    [Theory]
    [MemberData(nameof(CollectionComponents))]
    public void DifferentComponentsDoNotMerge(DataComponentType type)
    {
        var first = CreateComponent(type, 1);
        var second = CreateComponent(type, 2);
        var stored = new ItemStack(ItemsRegistry.Diamond, 2, first);
        var incoming = new ItemStack(ItemsRegistry.Diamond, 2, second);
        var inventory = new Container(46);
        inventory.SetItem(36, stored);

        var changed = PlayerEntity.AddToInventory(inventory, 36, incoming);

        Assert.True(first != second);
        Assert.False(first.Equals((object)second));
        Assert.True(stored != incoming);
        Assert.Equal([37], changed);
        Assert.Equal(2, inventory.GetItem(36)!.Count);
        Assert.Equal(2, inventory.GetItem(37)!.Count);
        Assert.Equal(0, incoming.Count);
    }

    [Theory]
    [MemberData(nameof(CollectionComponents))]
    public void DecodedComponentsKeepValueEqualityAfterHashing(DataComponentType type)
    {
        var component = CreateComponent(type, 1);
        var originalHash = component.GetHashCode();
        using var writer = new NetworkBuffer();
        component.Write(writer);
        using var reader = new NetworkBuffer(writer.AsSpan(0, writer.Size).ToArray());

        var decoded = component.Read(reader);

        // Opaque payloads have no decoder; their byte equality is tested by independent construction.
        Assert.Equal(component, decoded);
        Assert.Equal(originalHash, decoded.GetHashCode());
        Assert.Equal(component, component with { });
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 16)]
    [InlineData(true, 64)]
    public void CustomPotionsAndTippedArrowsRespectStackLimits(bool tippedArrow, int maxStackSize)
    {
        var item = tippedArrow ? ItemsRegistry.TippedArrow : ItemsRegistry.Potion;
        var first = CreatePotion(1);
        var second = CreatePotion(1);
        var stackSize = ComponentBuilder.MaxStackSize with { Value = maxStackSize };
        var stored = new ItemStack(item, 1, first, stackSize);
        var incoming = new ItemStack(item, 1, second, stackSize);
        var inventory = new Container(46);
        inventory.SetItem(36, stored);

        var equal = stored.Equals(incoming);
        var changed = PlayerEntity.AddToInventory(inventory, 36, incoming);

        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.True(equal);
        Assert.True(stored == incoming);
        Assert.Equal(stored.GetHashCode(), incoming.GetHashCode());
        Assert.Equal(0, incoming.Count);

        if (maxStackSize == 1)
        {
            Assert.Equal([37], changed);
            Assert.Equal(1, inventory.GetItem(36)!.Count);
            Assert.Equal(1, inventory.GetItem(37)!.Count);
        }
        else
        {
            Assert.Equal([36], changed);
            Assert.Equal(2, inventory.GetItem(36)!.Count);
            Assert.Null(inventory.GetItem(37));
        }
    }

    [Theory]
    [InlineData("potion")]
    [InlineData("color")]
    [InlineData("name")]
    [InlineData("effect")]
    [InlineData("hidden effect")]
    [InlineData("order")]
    [InlineData("length")]
    public void PotionEqualityIncludesEveryFieldAndEffectOrder(string field)
    {
        var original = CreatePotion(1);
        var firstEffect = original.CustomEffects[0];
        var changed = field switch
        {
            "potion" => original with { Potion = Potion.Water },
            "color" => original with { CustomColor = 42 },
            "name" => original with { CustomName = "different" },
            "effect" => original with { CustomEffects = [firstEffect with { Duration = 42 }, Effect(2)] },
            "hidden effect" => original with
            {
                CustomEffects = [firstEffect with { HiddenEffect = Effect(42) }, Effect(2)]
            },
            "order" => original with { CustomEffects = [Effect(2), firstEffect] },
            "length" => original with { CustomEffects = [firstEffect] },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };

        var equal = original == changed;

        Assert.False(equal);
        Assert.False(original.Equals((object)changed));
        Assert.False(original.Equals(null));
    }

    [Fact]
    public void MapDecorationEqualityIgnoresInsertionOrder()
    {
        var first = new MapDecorationDataComponent
        {
            Decorations = new Dictionary<string, MapDecoration>
            {
                ["first"] = Decoration(1),
                ["second"] = Decoration(2)
            }
        };
        var second = new MapDecorationDataComponent
        {
            Decorations = new Dictionary<string, MapDecoration>
            {
                ["second"] = Decoration(2),
                ["first"] = Decoration(1)
            }
        };

        var equal = first == second;

        Assert.True(equal);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void EmptyCollectionsAndOptionalCollectionsHaveConsistentEquality()
    {
        var empty = new PotionContentsDataComponent { CustomEffects = [] };
        var anotherEmpty = new PotionContentsDataComponent { CustomEffects = ImmutableArray<PotionEffectData>.Empty };
        var defaultEffects = empty with { CustomEffects = default };
        var missingEntities = ComponentBuilder.Equippable;
        var emptyEntities = missingEntities with { AllowedEntities = [] };

        var equal = empty == anotherEmpty;

        Assert.True(equal);
        Assert.Equal(empty.GetHashCode(), anotherEmpty.GetHashCode());
        Assert.Equal(empty, defaultEffects);
        Assert.Equal(empty.GetHashCode(), defaultEffects.GetHashCode());
        Assert.NotEqual(empty, CreatePotion(1));
        Assert.NotEqual(missingEntities, emptyEntities);
        Assert.NotEqual<DataComponent>(new CanBreakDataComponent(), new CanPlaceOnDataComponent());
    }

    [Theory]
    [InlineData("ids")]
    [InlineData("blocks")]
    [InlineData("partial component")]
    [InlineData("consume effect")]
    public void NestedCollectionRecordsCompareByValue(string record)
    {
        object CreateValue(int value) => record switch
        {
            "ids" => Ids(value),
            "blocks" => Predicate(value),
            "partial component" => new ComponentPredicate
            {
                IsPredicateType = true,
                TypeId = 1,
                Value = new NbtCompound { new NbtTag<int>("value", value) }
            },
            "consume effect" => Consume(value),
            _ => throw new ArgumentOutOfRangeException(nameof(record))
        };

        var first = CreateValue(1);
        var second = CreateValue(1);
        var different = CreateValue(2);

        var equal = first.Equals(second);

        Assert.True(equal);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.False(first.Equals(different));
    }

    [Theory]
    [InlineData("floats")]
    [InlineData("flags")]
    [InlineData("strings")]
    [InlineData("colors")]
    public void CustomModelEqualityIncludesEveryCollection(string field)
    {
        var original = (CustomModelDataComponent)CreateComponent(DataComponentType.CustomModelData, 1);
        var changed = field switch
        {
            "floats" => original with { Floats = [2, 3] },
            "flags" => original with { Flags = [false, false] },
            "strings" => original with { Strings = ["changed", "1"] },
            "colors" => original with { Colors = [2, 0x123456] },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };

        var equal = original == changed;

        Assert.False(equal);
    }

    private static DataComponent CreateComponent(DataComponentType type, int value) => type switch
    {
        DataComponentType.PotionContents => CreatePotion(value),
        DataComponentType.CustomModelData => new CustomModelDataComponent
        {
            Floats = [value, 3],
            Flags = [true, false],
            Strings = ["first", value.ToString()],
            Colors = [value, 0x123456]
        },
        DataComponentType.Fireworks => new FireworksDataComponent
        {
            FlightDuration = 1,
            Explosions = [new FireworkExplosion
            {
                Shape = 0,
                Colors = [value, 0x123456],
                FadeColors = [0x654321],
                HasTrail = true
            }]
        },
        DataComponentType.Tool => ComponentBuilder.Tool with
        {
            Rules = [new ToolRule { Blocks = Ids(value), Speed = 2, CorrectDropForBlocks = true }]
        },
        DataComponentType.Equippable => ComponentBuilder.Equippable with
        {
            Slot = EquipmentSlot.Helmet,
            AllowedEntities = [value, 3],
            AllowedEntitySet = Ids(value)
        },
        DataComponentType.Profile => new ProfileDataComponent
        {
            Username = "Player",
            Properties = [new SkinProperty { Name = "textures", Value = value.ToString(), Signature = "signature" }]
        },
        DataComponentType.WrittenBookContent => new WrittenBookDataComponent
        {
            Title = "Book",
            Author = "Author",
            Pages = [(ChatMessage.Simple(value.ToString()).AddExtra("extra"), ChatMessage.Simple("filtered"))]
        },
        DataComponentType.Bees => new BeesDataComponent { Occupants = [(Entity(value), 1, 2)] },
        DataComponentType.Consumable => ComponentBuilder.Consumable with
        {
            Effects = [new ConsumeEffect { Type = "minecraft:apply_effects", Effect = Consume(value) }]
        },
        DataComponentType.CanPlaceOn => new CanPlaceOnDataComponent { Predicates = [Predicate(value)] },
        DataComponentType.CanBreak => new CanBreakDataComponent { Predicates = [Predicate(value)] },
        DataComponentType.AttributeModifiers => new AttributeModifiersDataComponent
        {
            Value = [new AttributeModifier
            {
                Id = 0,
                Name = "test:modifier",
                Value = 1,
                Operation = AttributeOperation.Add,
                Slot = AttributeSlot.Any
            }],
            Displays = [(2, ChatMessage.Simple(value.ToString()).AddExtra("extra"))]
        },
        DataComponentType.BannerPatterns => new BannerPatternsDataComponent
        {
            Value = [new BannerPatternLayer { AssetId = "", TranslationKey = "", Color = Dye.Red }],
            RegistryIds = [value]
        },
        DataComponentType.MapDecorations => new MapDecorationDataComponent
        {
            Decorations = new Dictionary<string, MapDecoration> { ["marker"] = Decoration(value) }
        },
        DataComponentType.CustomData => new OpaqueDataComponent(type, [(byte)value, 3]),
        DataComponentType.BucketEntityData => new BucketEntityDataComponent
        {
            Value = new NbtCompound { new NbtTag<int>("value", value) }
        },
        DataComponentType.EntityData => Entity(value),
        DataComponentType.DeathProtection => ComponentBuilder.DeathProtection with { Value = [Consume(value)] },
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    private static PotionContentsDataComponent CreatePotion(int value) => new()
    {
        Potion = Potion.Awkward,
        CustomColor = 0x123456,
        CustomName = "custom",
        CustomEffects = [Effect(value) with { HiddenEffect = Effect(3) }, Effect(2)]
    };

    private static PotionEffectData Effect(int duration) => new()
    {
        Id = 1,
        Amplifier = 1,
        Duration = duration,
        Ambient = true,
        ShowParticles = true,
        ShowIcon = true
    };

    private static IdSet Ids(int value) => new() { Type = 3, Ids = [value, 3] };

    private static EntityDataComponent Entity(int value) => new(DataComponentType.EntityData)
    {
        RegistryId = 1,
        Data = new NbtCompound { new NbtTag<int>("value", value) }
    };

    private static ComponentConsumeEffect Consume(int value) => new() { Id = 0, Effects = [Effect(value)] };

    private static BlockPredicate Predicate(int value) => new()
    {
        Blocks = Ids(value),
        Properties = [new BlockProperty { Name = "age", IsExactMatch = true, ExactValue = "1" }],
        ExactComponents = [CreatePotion(value)],
        PartialComponents = [new ComponentPredicate
        {
            IsPredicateType = true,
            TypeId = 1,
            Value = new NbtCompound { new NbtTag<int>("value", value) }
        }],
        Nbt = new NbtCompound { new NbtTag<int>("value", value) }
    };

    private static MapDecoration Decoration(int value) => new()
    {
        Type = MapDecorationType.Player,
        X = value,
        Z = 2,
        Rotation = 3
    };
}
