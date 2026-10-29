using System.Text.Json;

namespace Obsidian.Registries;

/// <summary>
/// The data pack registries vanilla synchronizes during configuration, in the order it sends them, each with its
/// entries in network id order: the asset generator's <c>synced_registries.json</c>, taken from vanilla's own
/// registry synchronization. Clients number each registry's entries in the order they're sent.
/// </summary>
internal static class VanillaSyncedRegistries
{
    private static readonly Lazy<Registry[]> registries = new(Load);

    /// <summary>
    /// The registries, in the order vanilla sends them.
    /// </summary>
    public static IReadOnlyList<Registry> All => registries.Value;

    /// <summary>
    /// The network id of <paramref name="entry"/> in <paramref name="registry"/>, or -1 for an unknown entry.
    /// </summary>
    public static int IdOf(string registry, string entry) =>
        Array.Find(registries.Value, synced => synced.Name == registry) is Registry found
            ? Array.IndexOf(found.Entries, entry)
            : -1;

    /// <summary>
    /// Whether clients have <paramref name="registry"/> (like <c>minecraft:item</c>): it's built in, or a data pack
    /// registry vanilla syncs. Vanilla sends only these registries' tags; a client sent tags for another, like 26.3's
    /// <c>minecraft:villager_trade</c>, disconnects when configuration finishes.
    /// </summary>
    public static bool IsKnownToClients(string registry) =>
        VanillaRegistryIds.All.ContainsKey(registry) || Array.Exists(registries.Value, synced => synced.Name == registry);

    private static Registry[] Load()
    {
        using var stream = typeof(VanillaSyncedRegistries).Assembly
            .GetManifestResourceStream("Obsidian.Assets.synced_registries.json")!;

        return JsonSerializer.Deserialize<Registry[]>(stream, JsonSerializerOptions.Web)!;
    }

    /// <param name="Name">The registry, like <c>minecraft:world_clock</c>.</param>
    /// <param name="Entries">The registry's entries, by network id.</param>
    internal sealed record Registry(
        [property: System.Text.Json.Serialization.JsonPropertyName("registry")] string Name,
        string[] Entries);
}
