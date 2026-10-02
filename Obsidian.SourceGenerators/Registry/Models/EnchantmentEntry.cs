namespace Obsidian.SourceGenerators.Registry.Models;

/// <summary>
/// An enchantment from <c>Assets/enchantments.json</c>, so enchantment tags (<c>enchantment/exclusive_set</c>) resolve
/// to the network ids the enchantment registry is sent with.
/// </summary>
internal sealed class EnchantmentEntry(string tag, int registryId) : ITaggable
{
    public string Tag { get; } = tag;

    public string Type => "enchantment";

    public string Parent => "enchantment";

    public string GetTagValue() => registryId.ToString();
}
