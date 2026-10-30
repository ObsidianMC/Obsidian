using Obsidian.WorldData.Generators.Density;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Parity checks for the 26.3 noises and density functions, replaying the references in
/// <c>Assets/vanilla_worldgen_26.3.json</c> (<see cref="VanillaWorldgenCaptures.CurrentCases"/>). 26.3 evaluates them
/// in single precision, so every value must match vanilla's float bit for bit. Re-record them with
/// <c>dotnet build Obsidian.AssetGenerator -t:CaptureVanillaWorldgen</c> (<c>docs/vanilla-captures.md</c>).
/// </summary>
public class VanillaDensityFunctions
{
    // Java's Float.floatToIntBits of NaN, which the captures record for every NaN.
    private const int JavaNaNBits = 0x7FC00000;

    // Building a world's routers reads and seeds every noise they use, so each is built once.
    private static readonly ConcurrentDictionary<(long Seed, string Settings), DensityRouter> routers = new();

    public sealed record NoiseCase(long Seed, string Noise, double X, double Y, double Z, float Expected)
    {
        public override string ToString() => $"{this.Noise} at {this.X}, {this.Y}, {this.Z} (seed {this.Seed})";
    }

    public sealed record RouterCase(long Seed, string Settings, int X, int Y, int Z, string ExpectedJson)
    {
        public override string ToString() => $"{this.Settings} at {this.X}, {this.Y}, {this.Z} (seed {this.Seed})";
    }

    public sealed record FunctionCase(long Seed, string FunctionJson, int X, int Y, int Z, int ExpectedBits)
    {
        public override string ToString() => $"{this.FunctionJson} at {this.X}, {this.Y}, {this.Z} (seed {this.Seed})";
    }

    // Vanilla: RandomState.create(noises, seed, overworld).getOrCreateNoise(noise).get(x, y, z).
    public static TheoryData<NoiseCase> NoiseCases()
    {
        var cases = new TheoryData<NoiseCase>();

        foreach (var entry in VanillaWorldgenCaptures.CurrentCases("noises"))
        {
            cases.Add(new NoiseCase(
                entry.GetProperty("seed").GetInt64(),
                entry.GetProperty("noise").GetString()!,
                entry.GetProperty("x").GetDouble(),
                entry.GetProperty("y").GetDouble(),
                entry.GetProperty("z").GetDouble(),
                entry.GetProperty("value").GetSingle()));
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(NoiseCases))]
    public void NoiseMatchesVanilla(NoiseCase noiseCase)
    {
        var compiler = new DensityCompiler(WorldgenData.Vanilla, noiseCase.Seed, legacyRandomSource: false);
        var noise = compiler.Noise(noiseCase.Noise);

        var value = noise.Get(noiseCase.X, noiseCase.Y, noiseCase.Z);

        AssertSameFloat(noiseCase.Expected, value);
    }

    // Vanilla: RandomState.sampleBlockValueUncached(function, x, y, z) for each router and aquifer function of a
    // dimension's noise settings.
    public static TheoryData<RouterCase> RouterCases()
    {
        var cases = new TheoryData<RouterCase>();

        foreach (var entry in VanillaWorldgenCaptures.CurrentCases("router"))
        {
            cases.Add(new RouterCase(
                entry.GetProperty("seed").GetInt64(),
                entry.GetProperty("settings").GetString()!,
                entry.GetProperty("x").GetInt32(),
                entry.GetProperty("y").GetInt32(),
                entry.GetProperty("z").GetInt32(),
                entry.GetProperty("values").GetRawText()));
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(RouterCases))]
    public void RouterMatchesVanilla(RouterCase routerCase)
    {
        var router = Router(routerCase.Seed, routerCase.Settings);
        var expected = JsonSerializer.Deserialize<Dictionary<string, float>>(routerCase.ExpectedJson)!;

        var mismatches = new List<string>();
        foreach (var (field, value) in expected)
        {
            var actual = RouterFunction(router, field).Sample(routerCase.X, routerCase.Y, routerCase.Z);

            if (!SameFloat(value, actual))
                mismatches.Add(Mismatch(field, value, actual));
        }

        Assert.Empty(mismatches);
    }

    // Vanilla: DensityFunction.CODEC parses the case's inline function, then
    // RandomState.sampleBlockValueUncached(function, x, y, z) samples it with the overworld's random. The cases cover
    // what no router reaches: nested interpolations and pow, near the world border and with Java's special cases.
    public static TheoryData<FunctionCase> FunctionCases()
    {
        var cases = new TheoryData<FunctionCase>();

        foreach (var entry in VanillaWorldgenCaptures.CurrentCases("functions"))
        {
            // Hex, since the bits of NaN and the infinities have no JSON number.
            var bits = Convert.ToInt32(entry.GetProperty("bits").GetString(), 16);

            cases.Add(new FunctionCase(
                entry.GetProperty("seed").GetInt64(),
                JsonSerializer.Serialize(entry.GetProperty("function")),
                entry.GetProperty("x").GetInt32(),
                entry.GetProperty("y").GetInt32(),
                entry.GetProperty("z").GetInt32(),
                bits));
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(FunctionCases))]
    public void FunctionMatchesVanilla(FunctionCase functionCase)
    {
        var compiler = new DensityCompiler(WorldgenData.Vanilla, functionCase.Seed, legacyRandomSource: false);
        using var document = JsonDocument.Parse(functionCase.FunctionJson);
        var function = compiler.Compile(document.RootElement, "function");

        var value = function.Sample(functionCase.X, functionCase.Y, functionCase.Z);

        AssertSameFloat(BitConverter.Int32BitsToSingle(functionCase.ExpectedBits), value);
    }

    // Obsidian-only: the reader handles every registered function vanilla ships, including those no router reaches.
    [Fact]
    public void EveryVanillaDensityFunctionCompiles()
    {
        var compiler = new DensityCompiler(WorldgenData.Vanilla, 12345L, legacyRandomSource: false);
        var ids = VanillaDensityFunctionIds();

        foreach (var id in ids)
            compiler.Function(id).Sample(17, 63, -29);

        Assert.NotEmpty(ids);
    }

    // Floats must match bit for bit, so 0 and -0 differ. NaNs match each other: Java and .NET give NaNs different
    // sign and payload bits, which vanilla never looks at.
    private static bool SameFloat(float expected, float actual) => JavaBits(expected) == JavaBits(actual);

    private static int JavaBits(float value) =>
        float.IsNaN(value) ? JavaNaNBits : BitConverter.SingleToInt32Bits(value);

    private static void AssertSameFloat(float expected, float actual)
    {
        if (!SameFloat(expected, actual))
            Assert.Fail(Mismatch("value", expected, actual));
    }

    private static string Mismatch(string field, float expected, float actual)
    {
        var expectedBits = JavaBits(expected);
        var actualBits = JavaBits(actual);

        return FormattableString.Invariant(
            $"{field}: expected {expected:R} (0x{expectedBits:X8}), got {actual:R} (0x{actualBits:X8})");
    }

    // The identifiers of the embedded density functions, from their resource names
    // (Obsidian.Assets.worldgen_26_3.density_function.overworld.continents.json is minecraft:overworld/continents).
    private static List<string> VanillaDensityFunctionIds()
    {
        const string prefix = "Obsidian.Assets.worldgen_26_3.density_function.";
        var resources = typeof(DensityFunction).Assembly.GetManifestResourceNames();

        return resources
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal))
            .Select(name => name[prefix.Length..^".json".Length].Replace('.', '/'))
            .Select(path => $"minecraft:{path}")
            .ToList();
    }

    private static DensityRouter Router(long seed, string settingsId) => routers.GetOrAdd((seed, settingsId), key =>
    {
        var settings = NoiseGeneratorSettings.Read(WorldgenData.Vanilla, key.Settings);
        var compiler = new DensityCompiler(WorldgenData.Vanilla, key.Seed, settings.LegacyRandomSource);

        return DensityRouter.Create(settings, compiler);
    });

    // A router field as the capture names it: "final_density", or "aquifers.barrier" for an aquifer function.
    private static DensityFunction RouterFunction(DensityRouter router, string field)
    {
        const string aquiferPrefix = "aquifers.";

        if (!field.StartsWith(aquiferPrefix, StringComparison.Ordinal))
            return router.Functions[field];

        return router.Aquifers![field[aquiferPrefix.Length..]];
    }
}
