using Obsidian.API;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Nbt;
using System;
using System.Collections.Generic;
using Xunit;

namespace Obsidian.Tests;

public sealed partial class ItemComponentEqualityRegressions
{
    public static TheoryData<string> FloatingPointFields => new()
    {
        "model",
        "food",
        "cooldown",
        "tool-default",
        "tool-rule",
        "consume-seconds",
        "consume-sound",
        "effect-probability",
        "effect-diameter",
        "effect-sound",
        "map-x",
        "map-z",
        "map-rotation",
        "attribute",
        "instrument-duration",
        "instrument-range",
        "instrument-sound",
        "cooking-fuel",
        "brewing-fuel",
        "visibility",
        "equip-sound",
        "shear-sound",
        "jukebox-length",
        "jukebox-sound",
        "nbt-float",
        "nbt-double"
    };

    [Theory]
    [MemberData(nameof(FloatingPointFields))]
    public void SignedZeroPreventsMerging(string field)
    {
        var negativeSingle = BitConverter.Int32BitsToSingle(unchecked((int)0x80000000));
        var negativeDouble = BitConverter.Int64BitsToDouble(unchecked((long)0x8000000000000000));
        var first = FloatingComponent(field, 0.0f, 0.0);
        var second = FloatingComponent(field, negativeSingle, negativeDouble);

        AssertDoNotMerge(first, second);

        if (field == "model")
        {
            Assert.NotEqual(first.GetHashCode(), second.GetHashCode());
        }
    }

    [Theory]
    [MemberData(nameof(FloatingPointFields))]
    public void NaNPayloadsHaveEqualHashesAndMerge(string field)
    {
        var firstSingle = BitConverter.Int32BitsToSingle(0x7fc00001);
        var secondSingle = BitConverter.Int32BitsToSingle(unchecked((int)0xffc12345));
        var firstDouble = BitConverter.Int64BitsToDouble(0x7ff8000000000001);
        var secondDouble = BitConverter.Int64BitsToDouble(unchecked((long)0xfff8000000012345));
        var first = FloatingComponent(field, firstSingle, firstDouble);
        var second = FloatingComponent(field, secondSingle, secondDouble);

        AssertMerge(first, second);
    }

    [Theory]
    [InlineData(1.0f, 1.0f, true)]
    [InlineData(1.0f, 2.0f, false)]
    [InlineData(float.PositiveInfinity, float.PositiveInfinity, true)]
    [InlineData(float.PositiveInfinity, float.NegativeInfinity, false)]
    public void ModelFloatsRetainOtherJavaEqualityCases(float firstValue, float secondValue, bool equal)
    {
        var first = new CustomModelDataComponent { Floats = [firstValue] };
        var second = new CustomModelDataComponent { Floats = [secondValue] };

        if (equal)
        {
            AssertMerge(first, second);
        }
        else
        {
            AssertDoNotMerge(first, second);
        }
    }

    private static DataComponent FloatingComponent(string field, float single, double number) => field switch
    {
        "model" => new CustomModelDataComponent { Floats = [single] },
        "food" => ComponentBuilder.Food with { SaturationModifier = single },
        "cooldown" => new UseCooldownDataComponent { Seconds = single },
        "tool-default" => ComponentBuilder.Tool with { DefaultMiningSpeed = single },
        "tool-rule" => ComponentBuilder.Tool with
        {
            Rules = [new ToolRule
            {
                Blocks = EmptyBlockSet(),
                Speed = single
            }]
        },
        "consume-seconds" => ComponentBuilder.Consumable with { ConsumeSeconds = single },
        "consume-sound" => ComponentBuilder.Consumable with { Sound = FloatingSound(single) },
        "effect-probability" => ComponentBuilder.DeathProtection with
        {
            Value = [new ComponentConsumeEffect { Id = 0, Probability = single }]
        },
        "effect-diameter" => ComponentBuilder.DeathProtection with
        {
            Value = [new ComponentConsumeEffect { Id = 3, Diameter = single }]
        },
        "effect-sound" => ComponentBuilder.DeathProtection with
        {
            Value = [new ComponentConsumeEffect { Id = 4, Sound = FloatingSound(single) }]
        },
        "map-x" => FloatingDecoration(number, 1, 1),
        "map-z" => FloatingDecoration(1, number, 1),
        "map-rotation" => FloatingDecoration(1, 1, single),
        "attribute" => new AttributeModifiersDataComponent
        {
            Value = [new AttributeModifier
            {
                Id = 0,
                Name = "test:value",
                Value = number,
                Operation = 0,
                Slot = 0
            }]
        },
        "instrument-duration" => new InstrumentDataComponent { Value = new InstrumentData { UseDuration = single } },
        "instrument-range" => new InstrumentDataComponent { Value = new InstrumentData { Range = single } },
        "instrument-sound" => new InstrumentDataComponent
        {
            Value = new InstrumentData { SoundEvent = FloatingSound(single) }
        },
        "cooking-fuel" => ComponentBuilder.CookingFuel with
        {
            Value = new FuelComponentData(new ComponentIntValue(1), new ComponentFloatValue(single))
        },
        "brewing-fuel" => ComponentBuilder.BrewingFuel with
        {
            Value = new FuelComponentData(new ComponentIntValue(1), new ComponentFloatValue(single))
        },
        "visibility" => ComponentBuilder.MobVisibility with
        {
            Value = new MobVisibilityData(EmptyBlockSet(), single)
        },
        "equip-sound" => ComponentBuilder.Equippable with { EquipSound = FloatingSound(single) },
        "shear-sound" => ComponentBuilder.Equippable with { ShearingSound = FloatingSound(single) },
        "jukebox-length" => new JukeboxPlayableDataComponent
        {
            Song = new(new JukeboxSong { LengthInSeconds = single })
        },
        "jukebox-sound" => new JukeboxPlayableDataComponent
        {
            Song = new(new JukeboxSong { SoundEvent = FloatingSound(single) })
        },
        "nbt-float" => ComponentBuilder.CustomData with
        {
            Value = new NbtCompound { new NbtTag<float>("value", single) }
        },
        "nbt-double" => ComponentBuilder.CustomData with
        {
            Value = new NbtCompound { new NbtTag<double>("value", number) }
        },
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };

    private static SoundEvent FloatingSound(float value) => new()
    {
        ResourceLocation = "minecraft:entity.generic.eat",
        FixedRange = value
    };

    private static IdSet EmptyBlockSet() => new()
    {
        Type = 1,
        Ids = []
    };

    private static MapDecorationDataComponent FloatingDecoration(double x, double z, float rotation) => new()
    {
        Decorations = new Dictionary<string, MapDecoration>
        {
            ["marker"] = new MapDecoration
            {
                Type = 0,
                X = x,
                Z = z,
                Rotation = rotation
            }
        }
    };
}
