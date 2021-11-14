using System.Collections.Generic;

namespace Obsidian.Utilities;

public record Permission(string Name)
{
    public static readonly string Wildcard = "*";

    public List<Permission> Children { get; } = new();
}
