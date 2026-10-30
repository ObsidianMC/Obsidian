using Obsidian.Nbt;
using Obsidian.Providers;
using Obsidian.Providers.IntProviders;
using System.Text.Json;
using static Obsidian.Registries.Codecs.CodecJson;

namespace Obsidian.Registries.Codecs;

/// <summary>
/// Reads and writes int providers like vanilla's IntProvider codecs: a plain int for a constant, or an object with a
/// <c>type</c> (<see cref="IntProviderTypes"/>) and that type's fields.
/// </summary>
internal static class IntProviderCodec
{
    /// <summary>
    /// Reads a provider whose values must all be from <paramref name="min"/> to <paramref name="max"/>, like
    /// vanilla's IntProvider.codec(min, max), which checks the provider's lowest and highest values.
    /// </summary>
    public static IIntProvider Read(JsonElement value, string path, int min, int max)
    {
        var provider = Read(value, path);
        if (provider.MinValue >= min && provider.MaxValue <= max)
            return provider;

        var values = $"{provider.MinValue} to {provider.MaxValue}";
        throw Error(path, $"the provider's values ({values}) aren't from {min} to {max}");
    }

    public static IIntProvider Read(JsonElement value, string path)
    {
        if (value.ValueKind == JsonValueKind.Number)
            return new ConstantIntProvider { Value = Int(value, path) };

        Object(value, path);

        var type = Identifier(Required(value, path, "type"), Field(path, "type"));
        switch (type)
        {
            case IntProviderTypes.Constant:
                return new ConstantIntProvider { Value = ReadInt(value, path, "value") };
            case IntProviderTypes.Uniform:
            {
                var (min, max) = ReadRange(value, path);
                return new UniformIntProvider { MinInclusive = min, MaxInclusive = max };
            }
            case IntProviderTypes.BiasedToBottom:
            {
                var (min, max) = ReadRange(value, path);
                return new BiasedToBottomIntProvider { MinInclusive = min, MaxInclusive = max };
            }
            case IntProviderTypes.VeryBiasedToBottom:
            {
                var (min, max) = ReadRange(value, path);
                return new VeryBiasedToBottomIntProvider { MinInclusive = min, MaxInclusive = max };
            }
            case IntProviderTypes.Trapezoid:
            {
                var trapezoid = new TrapezoidIntProvider
                {
                    Min = ReadInt(value, path, "min"),
                    Max = ReadInt(value, path, "max"),
                    Plateau = ReadInt(value, path, "plateau")
                };

                ValidateTrapezoid(trapezoid, path);

                return trapezoid;
            }
            case IntProviderTypes.Clamped:
            {
                var source = Read(Required(value, path, "source"), Field(path, "source"));
                var (min, max) = ReadRange(value, path);
                return new ClampedIntProvider { Source = source, MinInclusive = min, MaxInclusive = max };
            }
            case IntProviderTypes.ClampedNormal:
            {
                var mean = Float(Required(value, path, "mean"), Field(path, "mean"));
                var deviation = Float(Required(value, path, "deviation"), Field(path, "deviation"));
                var (min, max) = ReadRange(value, path);
                return new ClampedNormalIntProvider
                {
                    Mean = mean,
                    Deviation = deviation,
                    MinInclusive = min,
                    MaxInclusive = max
                };
            }
            case IntProviderTypes.WeightedList:
                return new WeightedListIntProvider { Distribution = ReadDistribution(value, path) };
            default:
                throw Error(Field(path, "type"), $"\"{type}\" isn't an int provider type");
        }
    }

    /// <summary>
    /// Writes a provider as vanilla's IntProvider.CODEC encodes it: a constant as an int tag, others as a compound
    /// with the type's identifier and its fields.
    /// </summary>
    public static INbtTag ToNbt(IIntProvider provider, string name)
    {
        if (provider is ConstantIntProvider constant)
            return new NbtTag<int>(name, constant.Value);

        var compound = new NbtCompound(name)
        {
            new NbtTag<string>("type", provider.Type)
        };

        switch (provider)
        {
            case UniformIntProvider uniform:
                compound.Add(new NbtTag<int>("min_inclusive", uniform.MinInclusive));
                compound.Add(new NbtTag<int>("max_inclusive", uniform.MaxInclusive));
                break;
            case BiasedToBottomIntProvider biased:
                compound.Add(new NbtTag<int>("min_inclusive", biased.MinInclusive));
                compound.Add(new NbtTag<int>("max_inclusive", biased.MaxInclusive));
                break;
            case VeryBiasedToBottomIntProvider veryBiased:
                compound.Add(new NbtTag<int>("min_inclusive", veryBiased.MinInclusive));
                compound.Add(new NbtTag<int>("max_inclusive", veryBiased.MaxInclusive));
                break;
            case TrapezoidIntProvider trapezoid:
                ValidateTrapezoid(trapezoid, name);
                compound.Add(new NbtTag<int>("min", trapezoid.Min));
                compound.Add(new NbtTag<int>("max", trapezoid.Max));
                compound.Add(new NbtTag<int>("plateau", trapezoid.Plateau));
                break;
            case ClampedIntProvider clamped:
                compound.Add(ToNbt(clamped.Source, "source"));
                compound.Add(new NbtTag<int>("min_inclusive", clamped.MinInclusive));
                compound.Add(new NbtTag<int>("max_inclusive", clamped.MaxInclusive));
                break;
            case ClampedNormalIntProvider normal:
                compound.Add(new NbtTag<float>("mean", normal.Mean));
                compound.Add(new NbtTag<float>("deviation", normal.Deviation));
                compound.Add(new NbtTag<int>("min_inclusive", normal.MinInclusive));
                compound.Add(new NbtTag<int>("max_inclusive", normal.MaxInclusive));
                break;
            case WeightedListIntProvider weighted:
                var distribution = new NbtList(NbtTagType.Compound, "distribution");
                foreach (var entry in weighted.Distribution)
                {
                    distribution.Add(new NbtCompound
                    {
                        ToNbt(entry.Data, "data"),
                        new NbtTag<int>("weight", entry.Weight)
                    });
                }

                compound.Add(distribution);
                break;
        }

        return compound;
    }

    private static int ReadInt(JsonElement obj, string path, string name) =>
        Int(Required(obj, path, name), Field(path, name));

    private static void ValidateTrapezoid(TrapezoidIntProvider provider, string path)
    {
        if (provider.Max < provider.Min)
            throw Error(path, $"max ({provider.Max}) is less than min ({provider.Min})");

        // Vanilla requires plateau but allows negative values, and computes the width with 32-bit arithmetic.
        var range = provider.Max - provider.Min;
        if (provider.Plateau > range)
            throw Error(path, $"plateau ({provider.Plateau}) is more than max - min ({range})");
    }

    // min_inclusive and max_inclusive, which every ranged provider type checks are in order.
    private static (int Min, int Max) ReadRange(JsonElement obj, string path)
    {
        var min = ReadInt(obj, path, "min_inclusive");
        var max = ReadInt(obj, path, "max_inclusive");

        if (max < min)
            throw Error(path, $"max_inclusive ({max}) is less than min_inclusive ({min})");

        return (min, max);
    }

    // A weighted list's distribution: a non-empty list of {"data": provider, "weight": n}, weights not negative.
    private static ImmutableArray<WeightedEntry<IIntProvider>> ReadDistribution(JsonElement obj, string providerPath)
    {
        var path = Field(providerPath, "distribution");
        var list = Array(Required(obj, providerPath, "distribution"), path);
        if (list.GetArrayLength() == 0)
            throw Error(path, "the list can't be empty");

        var entries = ImmutableArray.CreateBuilder<WeightedEntry<IIntProvider>>();
        var total = 0L;

        foreach (var (item, i) in list.EnumerateArray().Select((item, i) => (item, i)))
        {
            var itemPath = Index(path, i);
            Object(item, itemPath);

            var weight = Int(Required(item, itemPath, "weight"), Field(itemPath, "weight"), 0, int.MaxValue);
            total += weight;

            entries.Add(new WeightedEntry<IIntProvider>
            {
                Data = Read(Required(item, itemPath, "data"), Field(itemPath, "data")),
                Weight = weight
            });
        }

        // Vanilla's WeightedList can't hold more than an int's worth of weight.
        if (total > int.MaxValue)
            throw Error(path, "the weights add up to more than 2147483647");

        return entries.ToImmutable();
    }
}
