using Obsidian.Tests.Utilities;
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

    // Vanilla: RandomState.create(noises, seed, overworld).getOrCreateNoise(noise).get(x, y, z).
    [Fact]
    public void NoisesMatchVanilla()
    {
        var cases = VanillaWorldgenCaptures.CurrentCases("noises");

        FixtureCases.AssertAll(cases, NoiseName, AssertNoise);
    }

    private static void AssertNoise(JsonElement entry)
    {
        var compiler = new DensityCompiler(WorldgenData.Vanilla, Seed(entry), legacyRandomSource: false);
        var noise = compiler.Noise(entry.GetProperty("noise").GetString()!);

        var value = noise.Get(
            entry.GetProperty("x").GetDouble(),
            entry.GetProperty("y").GetDouble(),
            entry.GetProperty("z").GetDouble());

        AssertSameFloat(entry.GetProperty("value").GetSingle(), value);
    }

    // Vanilla: RandomState.sampleBlockValueUncached(function, x, y, z) for each router and aquifer function of a
    // dimension's noise settings.
    [Fact]
    public void RoutersMatchVanilla()
    {
        var cases = VanillaWorldgenCaptures.CurrentCases("router");

        FixtureCases.AssertAll(cases, RouterName, AssertRouter);
    }

    private static void AssertRouter(JsonElement entry)
    {
        var router = Router(Seed(entry), entry.GetProperty("settings").GetString()!);
        var expected = JsonSerializer.Deserialize<Dictionary<string, float>>(entry.GetProperty("values"))!;
        var (x, y, z) = (Int(entry, "x"), Int(entry, "y"), Int(entry, "z"));

        var mismatches = new List<string>();
        foreach (var (field, value) in expected)
        {
            var actual = RouterFunction(router, field).Sample(x, y, z);

            if (!SameFloat(value, actual))
                mismatches.Add(Mismatch(field, value, actual));
        }

        Assert.Empty(mismatches);
    }

    // Vanilla: DensityFunction.CODEC parses the case's inline function, then
    // RandomState.sampleBlockValueUncached(function, x, y, z) samples it with the overworld's random. The cases cover
    // what no router reaches: nested interpolations and pow, near the world border and with Java's special cases.
    [Fact]
    public void FunctionsMatchVanilla()
    {
        var cases = VanillaWorldgenCaptures.CurrentCases("functions");

        FixtureCases.AssertAll(cases, FunctionName, AssertFunction);
    }

    private static void AssertFunction(JsonElement entry)
    {
        // Hex, since the bits of NaN and the infinities have no JSON number.
        var bits = Convert.ToInt32(entry.GetProperty("bits").GetString(), 16);
        var compiler = new DensityCompiler(WorldgenData.Vanilla, Seed(entry), legacyRandomSource: false);
        var function = compiler.Compile(entry.GetProperty("function"), "function");

        var value = function.Sample(Int(entry, "x"), Int(entry, "y"), Int(entry, "z"));

        AssertSameFloat(BitConverter.Int32BitsToSingle(bits), value);
    }

    private static long Seed(JsonElement entry) => entry.GetProperty("seed").GetInt64();

    private static int Int(JsonElement entry, string name) => entry.GetProperty(name).GetInt32();

    private static string NoiseName(JsonElement entry) => PointName(entry.GetProperty("noise").GetString()!, entry);

    private static string RouterName(JsonElement entry) => PointName(entry.GetProperty("settings").GetString()!, entry);

    private static string FunctionName(JsonElement entry) =>
        PointName(entry.GetProperty("function").GetRawText(), entry);

    private static string PointName(string subject, JsonElement entry) =>
        $"{subject} at {entry.GetProperty("x")}, {entry.GetProperty("y")}, {entry.GetProperty("z")} "
        + $"(seed {entry.GetProperty("seed")})";

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
