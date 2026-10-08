using Obsidian.Nbt.Interfaces;
using Obsidian.API.Utilities;

namespace Obsidian.API.Registry.Codecs.Dialogs;

internal static class DialogValidation
{
    internal static void Range(int value, int min, int max, string name)
    {
        if (value < min || value > max)
            throw new ArgumentOutOfRangeException(name, value, $"Must be between {min} and {max}.");
    }

    internal static void Identifier(string value)
    {
        var parts = value.Split(':');
        if (parts.Length != 2 || parts.Any(string.IsNullOrEmpty) ||
            parts[0].Any(c => !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-' or '.')) ||
            parts[1].Any(c => !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-' or '.' or '/')))
            throw new ArgumentException("Expected a namespaced Minecraft identifier.", nameof(value));
    }

    internal static void WriteText(INbtWriter writer, string name, ChatMessage text)
    {
        writer.WriteCompoundStart(name);
        writer.WriteChatMessage(text);
        writer.EndCompound();
    }
}
