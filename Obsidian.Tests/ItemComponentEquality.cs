using Obsidian.API;
using Obsidian.API.Effects;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Registries;
using Obsidian.Nbt;
using Obsidian.Net;
using Obsidian.Tests.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Xunit;
using PlayerEntity = Obsidian.Entities.Player;

namespace Obsidian.Tests;

public sealed partial class ItemComponentEquality
{
    // Components whose records hold collections, which C# compares by reference unless Obsidian compares them itself.
    private static readonly DataComponentType[] CollectionComponents =
    [
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
    ];

    // Stacks merge only when their components are equal, so a wrong equality either merges different items (losing
    // one's components) or splits equal ones.
    [Fact]
    public void CollectionComponentsMergeByValue()
    {
        FixtureCases.AssertAll(CollectionComponents, type => type.ToString(), AssertCollectionComponentMergesByValue);
    }

    private static void AssertCollectionComponentMergesByValue(DataComponentType type)
    {
        AssertEqualComponentsMerge(type);
        AssertDifferentComponentsDoNotMerge(type);
        AssertDecodedComponentsKeepValueEquality(type);
    }

    private static void AssertEqualComponentsMerge(DataComponentType type)
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

    private static void AssertDifferentComponentsDoNotMerge(DataComponentType type)
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

    private static void AssertDecodedComponentsKeepValueEquality(DataComponentType type)
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
