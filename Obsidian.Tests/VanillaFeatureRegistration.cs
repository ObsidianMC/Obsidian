using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Generators.Features;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Obsidian.Tests;

public sealed class VanillaFeatureRegistration
{
    [Fact]
    public void EveryVanillaFeatureCanBeCompiled()
    {
        const string prefix = "Obsidian.Assets.worldgen_26_3.feature.";
        var assembly = typeof(FeatureRegistry).Assembly;
        var resources = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(prefix, System.StringComparison.Ordinal))
            .OrderBy(name => name, System.StringComparer.Ordinal)
            .ToArray();
        var registry = new FeatureRegistry(WorldgenData.Vanilla);

        Assert.NotEmpty(resources);

        foreach (var resource in resources)
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var document = JsonDocument.Parse(stream);

            // Compilation descends into inline features, placed references, providers and predicates as well.
            Assert.NotNull(registry.Feature(document.RootElement));
        }
    }
}
