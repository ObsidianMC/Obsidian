using Obsidian.API.World.Generator.RandomSources;
using Obsidian.API.Utilities;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text.Json;

namespace Obsidian.API.Data;

/// <summary>
/// Reads 26.3 integer and float expressions. String values are registry references, numbers are constants,
/// and objects have a namespaced type. The generated Ints/Floats dictionaries contain vanilla's definitions.
/// </summary>
public static partial class ContextNumberProviders
{
    public static ContextIntProvider ParseInt(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ReadInt(document.RootElement);
    }

    public static ContextFloatProvider ParseFloat(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ReadFloat(document.RootElement);
    }

    public static ContextIntProvider ReadInt(JsonElement json) => new(CompileInt(json));

    public static ContextFloatProvider ReadFloat(JsonElement json) => new(CompileFloat(json));

    private static Func<NumberContext, int> CompileInt(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.Number)
        {
            var value = ReadIntegerConstant(json);
            return _ => value;
        }

        if (json.ValueKind == JsonValueKind.String)
        {
            var id = Qualify(json.GetString()!);
            return context => ResolveIntReference(context, id);
        }

        var type = Type(json);
        switch (type)
        {
            case "constant":
                return CompileInt(json.GetProperty("value"));
            case "from_float":
                var fromFloat = CompileFloat(json.GetProperty("input"));
                return context => ToInt(fromFloat(context));
            case "uniform":
                var min = CompileInt(json.GetProperty("min"));
                var max = CompileInt(json.GetProperty("max"));
                return context =>
                {
                    var lower = min(context);
                    var upper = max(context);
                    return lower >= upper ? lower : context.Random.NextInt(unchecked(upper - lower + 1)) + lower;
                };
            case "binomial":
                var trials = CompileInt(json.GetProperty("n"));
                var probability = CompileFloat(json.GetProperty("p"));
                return context =>
                {
                    var n = trials(context);
                    var p = probability(context);
                    if (!float.IsFinite(p))
                        throw new ArithmeticException("Binomial probability must be finite.");

                    var successes = 0;
                    // Matches 26.3: evaluate n then p, then one float draw per trial, even at p=0 or p=1.
                    for (var i = 0; i < n; i++)
                    {
                        if (context.Random.NextFloat() < p)
                            successes++;
                    }

                    return successes;
                };
            case "conditional":
                return Conditional(json, CompileInt);
            case "number_dispatcher":
                return Dispatcher(json, CompileInt);
            case "weighted_list":
                return Weighted(json, CompileInt);
            case "add" or "mul" or "avg" or "min" or "max":
                var inputs = Inputs(json, CompileInt);
                return context =>
                {
                    long result = type switch
                    {
                        "mul" => 1,
                        "min" => int.MaxValue,
                        "max" => int.MinValue,
                        _ => 0
                    };

                    foreach (var input in inputs)
                    {
                        var value = input(context);
                        result = type switch
                        {
                            "min" => Math.Min(result, value),
                            "max" => Math.Max(result, value),
                            "mul" => unchecked(result * value),
                            _ => unchecked(result + value)
                        };
                    }

                    // Vanilla accumulates integer sum/product/average in a long and checks the final int range.
                    return checked((int)(type == "avg" ? result / inputs.Length : result));
                };
            case "abs" or "negate":
                var unary = CompileInt(json.GetProperty("input"));
                return context => type == "abs" ? Math.Abs(unary(context)) : checked(-unary(context));
            case "sub" or "div" or "mod" or "floor_div" or "floor_mod" or "pow":
                var left = CompileInt(json.GetProperty(type == "pow" ? "base" : "left"));
                var right = CompileInt(json.GetProperty(type == "pow" ? "exponent" : "right"));
                return context => IntegerBinary(type, left(context), right(context));
            case "environment_attribute":
                var attribute = Qualify(json.GetProperty("attribute").GetString()!);
                return context => context.IntEnvironmentAttribute?.Invoke(attribute) ?? 0;
            case "storage":
                var storage = json.GetProperty("storage").GetString()!;
                var path = json.GetProperty("path").GetString()!;
                var fallback = CompileInt(json.GetProperty("fallback"));
                return context => context.IntStorage?.Invoke(storage, path) ?? fallback(context);
            case "score":
                var target = json.GetProperty("target").Clone();
                var score = json.GetProperty("score").GetString()!;
                var scoreFallback = CompileInt(json.GetProperty("fallback"));
                return context => context.Score?.Invoke(target, score) ?? scoreFallback(context);
            default:
                throw new InvalidDataException($"Unsupported integer provider {type}.");
        }
    }

    private static Func<NumberContext, float> CompileFloat(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.Number)
        {
            var value = json.GetSingle();
            if (!float.IsFinite(value))
                throw new InvalidDataException("A float constant must be finite.");

            return _ => value;
        }

        if (json.ValueKind == JsonValueKind.String)
        {
            var id = Qualify(json.GetString()!);
            return context => ResolveFloatReference(context, id);
        }

        var type = Type(json);
        switch (type)
        {
            case "constant":
                return CompileFloat(json.GetProperty("value"));
            case "from_int":
                var fromInt = CompileInt(json.GetProperty("input"));
                return context => fromInt(context);
            case "uniform":
                var min = CompileFloat(json.GetProperty("min"));
                var max = CompileFloat(json.GetProperty("max"));
                return context =>
                {
                    var lower = min(context);
                    var upper = max(context);
                    return lower >= upper ? lower : context.Random.NextFloat() * (upper - lower) + lower;
                };
            case "conditional":
                return Conditional(json, CompileFloat);
            case "number_dispatcher":
                return Dispatcher(json, CompileFloat);
            case "weighted_list":
                return Weighted(json, CompileFloat);
            case "add" or "mul" or "avg" or "min" or "max" or "length":
                var inputs = Inputs(json, CompileFloat);
                return context =>
                {
                    var result = type switch
                    {
                        "mul" => 1f,
                        "min" => float.MaxValue,
                        "max" => -float.MaxValue,
                        _ => 0f
                    };

                    foreach (var input in inputs)
                    {
                        var value = input(context);
                        result = type switch
                        {
                            "min" => MathF.Min(result, value),
                            "max" => MathF.Max(result, value),
                            "mul" => result * value,
                            "length" => result + value * value,
                            _ => result + value
                        };
                    }

                    return type switch
                    {
                        "avg" => result / inputs.Length,
                        "length" => MathF.Sqrt(result),
                        _ => result
                    };
                };
            case "abs" or "negate" or "floor" or "ceil" or "round" or "truncate" or "sqrt" or "sin" or "cos":
                var unary = CompileFloat(json.GetProperty("input"));
                return context => FloatUnary(type, unary(context));
            case "sub" or "div" or "mod" or "pow":
                var left = CompileFloat(json.GetProperty(type == "pow" ? "base" : "left"));
                var right = CompileFloat(json.GetProperty(type == "pow" ? "exponent" : "right"));
                return context =>
                {
                    var a = left(context);
                    var b = right(context);
                    return type switch
                    {
                        "sub" => a - b,
                        "div" => a / b,
                        "mod" => a % b,
                        _ => FloatPower(a, b)
                    };
                };
            case "environment_attribute":
                var attribute = Qualify(json.GetProperty("attribute").GetString()!);
                return context => context.FloatEnvironmentAttribute?.Invoke(attribute) ?? 0;
            case "storage":
                var storage = json.GetProperty("storage").GetString()!;
                var path = json.GetProperty("path").GetString()!;
                var fallback = CompileFloat(json.GetProperty("fallback"));
                return context => context.FloatStorage?.Invoke(storage, path) ?? fallback(context);
            case "enchantment_level":
                var amount = ReadLevelValue(json.GetProperty("amount"));
                return context => amount(context.EnchantmentLevel ?? 0);
            default:
                throw new InvalidDataException($"Unsupported float provider {type}.");
        }
    }

    private static Func<NumberContext, T> Conditional<T>(
        JsonElement json,
        Func<JsonElement, Func<NumberContext, T>> compile)
    {
        var condition = json.GetProperty("condition").Clone();
        var onTrue = compile(json.GetProperty("on_true"));
        var onFalse = compile(json.GetProperty("on_false"));
        return context =>
        {
            var selected = Test(context, condition) ? onTrue : onFalse;
            return selected(context);
        };
    }

    private static Func<NumberContext, T> Dispatcher<T>(
        JsonElement json,
        Func<JsonElement, Func<NumberContext, T>> compile)
    {
        var cases = json.GetProperty("cases").EnumerateArray()
            .Select(row => CompileCase(row, compile))
            .ToArray();
        var fallback = compile(json.GetProperty("default"));
        return context =>
        {
            foreach (var row in cases)
            {
                if (Test(context, row.Condition))
                    return row.Value(context);
            }

            return fallback(context);
        };
    }

    private static Func<NumberContext, T> Weighted<T>(
        JsonElement json,
        Func<JsonElement, Func<NumberContext, T>> compile)
    {
        var rows = json.GetProperty("distribution").EnumerateArray()
            .Select(row => CompileWeightedValue(row, compile))
            .ToArray();
        var total = rows.Sum(row => (long)row.Weight);
        if (rows.Any(row => row.Weight < 0) || total <= 0 || total > int.MaxValue)
            throw new InvalidDataException(
                "A weighted provider needs a positive total weight within the integer range.");

        return context =>
        {
            // Vanilla's weighted list always makes a selection draw, including a single positive entry.
            var pick = context.Random.NextInt((int)total);
            foreach (var row in rows)
            {
                pick -= row.Weight;
                if (pick < 0)
                    return row.Value(context);
            }

            throw new InvalidOperationException("Invalid weighted provider selection.");
        };
    }

    private static Func<NumberContext, T>[] Inputs<T>(
        JsonElement json,
        Func<JsonElement, Func<NumberContext, T>> compile)
    {
        var inputs = json.GetProperty("inputs");
        if (inputs.ValueKind == JsonValueKind.Array)
            return inputs.EnumerateArray().Select(compile).ToArray();

        return [compile(inputs)];
    }

    private static bool Test(NumberContext context, JsonElement condition)
    {
        if (context.TestCondition is null)
            throw new InvalidOperationException("This provider requires a predicate evaluator.");

        return context.TestCondition(condition);
    }

    private static (JsonElement Condition, Func<NumberContext, T> Value) CompileCase<T>(
        JsonElement row,
        Func<JsonElement, Func<NumberContext, T>> compile)
    {
        var condition = row.GetProperty("condition").Clone();
        var value = compile(row.GetProperty("value"));
        return (condition, value);
    }

    private static (Func<NumberContext, T> Value, int Weight) CompileWeightedValue<T>(
        JsonElement row,
        Func<JsonElement, Func<NumberContext, T>> compile)
    {
        var value = compile(row.GetProperty("data"));
        var weight = row.GetProperty("weight").GetInt32();
        return (value, weight);
    }

    private static int ResolveIntReference(NumberContext context, string id)
    {
        var provider = context.ResolveInt(id);
        if (provider is null)
            throw new InvalidDataException($"Unknown integer provider {id}.");

        return context.Resolve(() => provider.Evaluate(context));
    }

    private static float ResolveFloatReference(NumberContext context, string id)
    {
        var provider = context.ResolveFloat(id);
        if (provider is null)
            throw new InvalidDataException($"Unknown float provider {id}.");

        return context.Resolve(() => provider.Evaluate(context));
    }

    private static Func<int, float> ReadLevelValue(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.Number)
        {
            var constant = json.GetSingle();
            return _ => constant;
        }

        if (Type(json) != "linear")
            throw new InvalidDataException("Only constant and linear enchantment level values are supported.");

        var start = json.GetProperty("base").GetSingle();
        var step = json.GetProperty("per_level_above_first").GetSingle();
        return level => start + step * (level - 1);
    }

    private static string Qualify(string id) => id.Contains(':') ? id : "minecraft:" + id;

    private static string Type(JsonElement json)
    {
        var id = Qualify(json.GetProperty("type").GetString()!);
        if (!id.StartsWith("minecraft:", StringComparison.Ordinal))
            throw new InvalidDataException($"Unknown context provider {id}.");

        return id["minecraft:".Length..];
    }

    private static int ReadIntegerConstant(JsonElement json)
    {
        // Vanilla's JSON integer codec uses Number.intValue: discard the fraction and retain the low 32 bits.
        // Parse decimal digits directly so large literals do not lose precision through a double.
        var text = json.GetRawText();
        var exponentIndex = text.IndexOfAny(['e', 'E']);
        var exponent = 0;
        if (exponentIndex >= 0)
        {
            exponent = int.Parse(text.AsSpan(exponentIndex + 1), CultureInfo.InvariantCulture);
            text = text[..exponentIndex];
        }

        var decimalIndex = text.IndexOf('.');
        var fractionLength = decimalIndex < 0 ? 0 : text.Length - decimalIndex - 1;
        var digits = text.Replace(".", "");
        var signLength = digits.StartsWith('-') ? 1 : 0;
        var scale = (long)fractionLength - exponent;
        if (scale >= digits.Length - signLength)
            return 0;

        if (scale > 0)
            digits = digits[..^(int)scale];

        // Any integer multiplied by 10^32 has zero low 32 bits.
        if (scale <= -32)
            return 0;

        var integer = BigInteger.Parse(digits, CultureInfo.InvariantCulture);
        if (scale < 0)
            integer *= BigInteger.Pow(10, (int)-scale);

        var lowBits = (uint)(integer & uint.MaxValue);
        return unchecked((int)lowBits);
    }

    private static int ToInt(float value)
    {
        if (!float.IsFinite(value) || (double)value < int.MinValue || (double)value >= (double)int.MaxValue + 1)
            throw new ArithmeticException("Float value is outside the integer range.");

        return (int)value;
    }

    private static int IntegerBinary(string type, int a, int b)
    {
        if (type == "sub")
            return checked(a - b);

        if (type == "pow")
        {
            if (b < 0 || a == 0 && b == 0)
                throw new ArithmeticException("Undefined integer power.");

            var result = 1;
            while (b > 0)
            {
                if ((b & 1) != 0)
                    result = checked(result * a);

                b >>= 1;
                if (b != 0)
                    a = checked(a * a);
            }

            return result;
        }

        // Match Java division's MIN_VALUE / -1 result; vanilla's floor_div uses floorDivExact instead.
        if (a == int.MinValue && b == -1)
        {
            if (type == "floor_div")
                throw new ArithmeticException("Integer floor division overflow.");

            return type is "mod" or "floor_mod" ? 0 : int.MinValue;
        }

        var quotient = a / b;
        var remainder = a % b;
        if (type == "floor_div")
            return remainder != 0 && (a ^ b) < 0 ? quotient - 1 : quotient;

        if (type == "floor_mod")
            return remainder != 0 && (a ^ b) < 0 ? remainder + b : remainder;

        return type == "div" ? quotient : remainder;
    }

    private static float FloatPower(float value, float exponent)
    {
        if (value == 0 && exponent == 0)
            return float.NaN;

        return (float)JavaMath.Pow(value, exponent);
    }

    // Match Java Math.round(float), including saturation and NaN.
    private static float Round(float value)
    {
        if (float.IsNaN(value))
            return 0;

        if (value >= int.MaxValue)
            return int.MaxValue;

        if (value <= int.MinValue)
            return int.MinValue;

        var rounded = Math.Floor((double)value + 0.5);
        return (int)rounded;
    }

    private static float Ceiling(float value)
    {
        // 26.3's ceiling provider uses Mth.ceil's integer result, unlike floor's floating-point result.
        var rounded = Math.Ceiling(value);
        var integer = (int)rounded;
        return integer;
    }

    private static float FloatUnary(string type, float value) => type switch
    {
        "abs" => MathF.Abs(value),
        "negate" => -value,
        "floor" => MathF.Floor(value),
        "ceil" => Ceiling(value),
        "round" => Round(value),
        "truncate" => MathF.Truncate(value),
        "sqrt" => MathF.Sqrt(value),
        // Match vanilla's table-based trigonometry, rather than the platform's transcendental functions.
        "sin" => Mth.Sin(value),
        _ => Mth.Cos(value)
    };
}
