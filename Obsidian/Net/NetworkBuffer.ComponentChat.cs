using System.Globalization;
using Obsidian.API.Utilities;
using System.IO;

namespace Obsidian.Net;

public partial class NetworkBuffer
{
    // NbtOps represents heterogeneous lists by wrapping non-compound elements in {"": value} compounds.
    // The general-purpose NBT object model forbids empty keys, so chat needs a projection that accepts them.
    private object? ReadChatNbt(int tag, int depth = 0)
    {
        if (depth > 64)
            throw new InvalidDataException("Chat nesting exceeds 64.");

        switch (tag)
        {
            case 0: return null;
            case 1: return this.ReadByte();
            case 2: return this.ReadShort();
            case 3: return this.ReadInt();
            case 4: return this.ReadLong();
            case 5: return this.ReadSingle();
            case 6: return this.ReadDouble();
            case 8: return this.ReadNbtString();
            case 10:
                var fields = new Dictionary<string, object?>();
                int next;
                while ((next = this.ReadByte()) != 0)
                    fields[this.ReadNbtString()] = this.ReadChatNbt(next, depth + 1);

                return fields;
            case 7: case 9: case 11: case 12:
                var element = tag == 9 ? this.ReadByte() : tag == 7 ? 1 : tag == 11 ? 3 : 4;
                var length = this.ReadInt();
                if (length < 0 || length > 1048576)
                    throw new InvalidDataException("Invalid chat list length.");

                var values = new object?[length];
                for (var i = 0; i < length; i++)
                    values[i] = this.ReadChatNbt(element, depth + 1);

                return values;
            default: throw new InvalidDataException("Invalid chat NBT tag.");
        }
    }

    private static ChatMessage ProjectChat(object? value)
    {
        if (value is string text)
            return ChatMessage.Simple(text);

        if (value is object?[] list)
        {
            var head = list.Length == 0 ? ChatMessage.Simple("") : ProjectChat(list[0]);
            foreach (var child in list.Skip(1))
                head.AddExtra(ProjectChat(child));

            return head;
        }

        if (value is not Dictionary<string, object?> fields)
            return ChatMessage.Simple(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");
        if (fields.Count == 1 && fields.TryGetValue("", out var wrapped))
            return ProjectChat(wrapped);

        string? String(string key) => fields.GetValueOrDefault(key) as string;
        bool? Flag(string key) => fields.GetValueOrDefault(key) is byte flag ? flag != 0 : null;

        var result = new ChatMessage
        {
            Text = String("text"),
            Translate = String("translate"),
            Insertion = String("insertion"),
            Color = String("color") is string color ? new HexColor(color) : null,
            Bold = Flag("bold"),
            Italic = Flag("italic"),
            Underlined = Flag("underlined"),
            Strikethrough = Flag("strikethrough"),
            Obfuscated = Flag("obfuscated"),
            ShadowColor = fields.GetValueOrDefault("shadow_color") as int?
        };

        if (fields.GetValueOrDefault("shadow_color") is object?[] { Length: 4 } rgba)
        {
            var channels = rgba
                .Select(channel => (uint)Math.Clamp(Convert.ToDouble(channel, CultureInfo.InvariantCulture) * 255, 0, 255))
                .ToArray();
            result.ShadowColor = unchecked((int)(channels[3] << 24 | channels[0] << 16 | channels[1] << 8 | channels[2]));
        }

        if (fields.GetValueOrDefault("click_event") is Dictionary<string, object?> click
            && click.GetValueOrDefault("action") is string action
            && Enum.TryParse<ClickAction>(action.ToPascalCase(), out var clickAction))
        {
            var argument = click.GetValueOrDefault("value") ?? click.GetValueOrDefault("url") ?? click.GetValueOrDefault("command")
                ?? click.GetValueOrDefault("path") ?? click.GetValueOrDefault("page");
            if (argument is not null)
                result.ClickEvent = new() { Action = clickAction, Value = Convert.ToString(argument, CultureInfo.InvariantCulture)! };
        }

        if (fields.GetValueOrDefault("hover_event") is Dictionary<string, object?> hover
            && hover.GetValueOrDefault("action") is "show_text")
        {
            result.HoverEvent = new()
            {
                Action = HoverAction.ShowText,
                Contents = new HoverChatContent
                {
                    ChatMessage = ProjectChat(hover.GetValueOrDefault("value") ?? hover.GetValueOrDefault("contents"))
                }
            };
        }

        if (fields.GetValueOrDefault("extra") is object?[] extras)
        {
            foreach (var child in extras)
                result.AddExtra(ProjectChat(child));
        }

        if (fields.GetValueOrDefault("with") is object?[] arguments)
        {
            foreach (var child in arguments)
                result.AddChatComponent(ProjectChat(child));
        }

        return result;
    }
}
