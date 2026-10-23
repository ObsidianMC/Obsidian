using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Obsidian.API;

/// <summary>
/// A namespaced identifier, like <c>minecraft:entity.pig.ambient</c>. One read without a namespace is vanilla's.
/// </summary>
public readonly partial record struct ResourceLocation(string Namespace, string Path)
{
    public const string DefaultNamespace = "minecraft";

    /// <summary>
    /// Reads an identifier, which like vanilla's has lowercase letters, digits and <c>_-.</c>, and <c>/</c> in its path.
    /// </summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out ResourceLocation? location)
    {
        location = null;

        if (text is null || !Pattern().IsMatch(text))
            return false;

        var separator = text.IndexOf(':');
        location = separator < 0
            ? new ResourceLocation(DefaultNamespace, text)
            : new ResourceLocation(separator == 0 ? DefaultNamespace : text[..separator], text[(separator + 1)..]);
        return true;
    }

    public override string ToString() => $"{this.Namespace}:{this.Path}";

    [GeneratedRegex(@"^([a-z0-9_.-]*:)?[a-z0-9_./-]+$")]
    private static partial Regex Pattern();
}
