namespace Obsidian.WorldData.Structures;

/// <summary>Portable identifiers, checked before generated files or resources are looked up.</summary>
internal static class StructureTemplateNames
{
    internal static bool IsValid(string name)
    {
        var colon = name.IndexOf(':');
        if (colon <= 0)
            return false;

        var ns = name[..colon];
        var path = name[(colon + 1)..];
        if (!ns.All(Allowed) || ns is "." or ".." || !Portable(ns))
            return false;

        // 26.3's TemplatePathFactory validates the converter's filename, after appending .nbt. Thus an empty
        // identifier path or a trailing slash produces a valid .nbt filename; empty/dot directory segments do not.
        foreach (var part in (path + ".nbt").Split('/'))
        {
            if (part.Length == 0 || part is "." or ".." || !part.All(Allowed) || !Portable(part))
                return false;
        }

        return true;
    }

    private static bool Allowed(char character) =>
        character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_' or '.';

    // FileUtil.isPathPartPortable applies the same Windows device-name rules on every operating system.
    private static bool Portable(string part)
    {
        if (part.EndsWith('.'))
            return false;

        var basename = part.Split('.', 2)[0];
        if (basename is "com" or "con" or "prn" or "aux" or "nul" or "clock$")
            return false;

        return !(basename.Length == 4 && basename[3] is >= '1' and <= '9'
            && (basename.StartsWith("com", StringComparison.Ordinal)
                || basename.StartsWith("lpt", StringComparison.Ordinal)));
    }
}
