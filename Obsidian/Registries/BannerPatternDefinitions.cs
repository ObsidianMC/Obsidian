using Obsidian.API;
using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;

namespace Obsidian.Registries;

/// <summary>
/// Vanilla 1.21.11's banner pattern definitions (<c>Assets/banner_patterns.json</c>): the asset and translation key each
/// pattern ID (for example <c>minecraft:stripe_top</c>) is drawn with. Loaded once.
/// </summary>
internal static class BannerPatternDefinitions
{
    public readonly record struct Definition(string AssetId, string TranslationKey);

    private static readonly Lazy<FrozenDictionary<string, Definition>> definitions = new(Load);

    /// <summary>The definition of a pattern, or null for an ID vanilla doesn't define.</summary>
    public static Definition? Get(string patternId) => definitions.Value.TryGetValue(patternId, out var found) ? found : null;

    /// <summary>The dye a saved banner layer names (for example <c>light_blue</c>), or null for an unknown color.</summary>
    public static Dye? ParseColor(string color) =>
        Enum.TryParse<Dye>(color.Replace("_", string.Empty), ignoreCase: true, out var dye) ? dye : null;

    private static FrozenDictionary<string, Definition> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Obsidian.Assets.banner_patterns.json")
            ?? throw new InvalidOperationException("Obsidian.Assets.banner_patterns.json is missing; run Obsidian.AssetGenerator.");
        using var document = JsonDocument.Parse(stream);

        var result = new Dictionary<string, Definition>();
        foreach (var pattern in document.RootElement.EnumerateObject())
        {
            result[pattern.Name] = new Definition(
                pattern.Value.GetProperty("asset_id").GetString()!,
                pattern.Value.GetProperty("translation_key").GetString()!);
        }

        return result.ToFrozenDictionary();
    }
}
