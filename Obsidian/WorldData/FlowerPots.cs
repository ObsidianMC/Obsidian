using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;
using Obsidian.API.Inventory;

namespace Obsidian.WorldData;

/// <summary>
/// What each flower pot holds, from vanilla's <c>FlowerPotBlock.getPotted</c> (<c>flower_pot_contents.json</c>): the
/// empty pot holds air, and every potted block one plant.
/// </summary>
internal static class FlowerPots
{
    // Each pot's plant, and each plant's pot, by block registry id.
    private static readonly Lazy<(FrozenDictionary<int, int> Contents, FrozenDictionary<int, int> Pots)> tables =
        new(Load);

    /// <summary>
    /// The plant <paramref name="pot"/> holds, as the item taking it out gives, or null for an empty pot or another
    /// block.
    /// </summary>
    public static ItemStack? ContentOf(IBlock pot)
    {
        if (!tables.Value.Contents.TryGetValue(pot.RegistryId, out var content) || content == BlockIds.Air)
            return null;

        var plant = BlocksRegistry.GetDefault(content);
        return BlocksRegistry.TryGetItem(plant, out var item) ? new ItemStack(item, 1) : null;
    }

    /// <summary>Whether <paramref name="pot"/> is a flower pot that holds nothing.</summary>
    public static bool IsEmpty(IBlock pot) =>
        tables.Value.Contents.TryGetValue(pot.RegistryId, out var content) && content == BlockIds.Air;

    /// <summary>
    /// The potted block planting <paramref name="stack"/> gives, like vanilla's <c>POTTED_BY_CONTENT</c> for the block
    /// the item places, or null when it can't be potted.
    /// </summary>
    public static IBlock? PotFor(ItemStack stack)
    {
        if (!BlocksRegistry.TryGetBlock(stack.Holder, out var plant))
            return null;

        return tables.Value.Pots.TryGetValue(plant.RegistryId, out var pot) ? BlocksRegistry.GetDefault(pot) : null;
    }

    private static (FrozenDictionary<int, int>, FrozenDictionary<int, int>) Load()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Obsidian.Assets.flower_pot_contents.json")
            ?? throw new InvalidOperationException("Missing flower_pot_contents.json asset.");
        var names = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;

        var contents = new Dictionary<int, int>();
        var pots = new Dictionary<int, int>();
        foreach (var (pot, content) in names)
        {
            var potId = BlocksRegistry.Get(pot).RegistryId;
            var contentId = BlocksRegistry.Get(content).RegistryId;
            contents[potId] = contentId;

            if (contentId != BlockIds.Air)
                pots[contentId] = potId;
        }

        return (contents.ToFrozenDictionary(), pots.ToFrozenDictionary());
    }
}
