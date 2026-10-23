using System.Text.Json;

namespace Obsidian.Registries;

/// <summary>
/// Every vanilla built-in registry's identifiers, indexed by protocol id: the asset generator's
/// <c>registry_ids.json</c>, from the server jar's registries report.
/// </summary>
internal static class VanillaRegistryIds
{
    private static readonly Lazy<Dictionary<string, string[]>> registries = new(Load);

    /// <summary>
    /// The registries by name, like <c>minecraft:particle_type</c>.
    /// </summary>
    public static IReadOnlyDictionary<string, string[]> All => registries.Value;

    /// <summary>
    /// The identifiers of <paramref name="registry"/>, indexed by id.
    /// </summary>
    public static IReadOnlyList<string> Get(string registry) => registries.Value[registry];

    private static Dictionary<string, string[]> Load()
    {
        using var stream = typeof(VanillaRegistryIds).Assembly
            .GetManifestResourceStream("Obsidian.Assets.registry_ids.json")!;

        return JsonSerializer.Deserialize<Dictionary<string, string[]>>(stream)!;
    }
}
