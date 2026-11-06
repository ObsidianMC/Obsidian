using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;
using System.Globalization;
using System.Text.Json;

namespace Obsidian.API.Utilities;
public partial class Extensions
{
    public static void WriteChatMessage(this INbtWriter writer, ChatMessage chatMessage)
    {
        if (!chatMessage.Text.IsNullOrEmpty() || chatMessage.Translate is null && chatMessage.Keybind is null)
            writer.WriteString("text", chatMessage.Text ?? string.Empty);
        if (!chatMessage.Translate.IsNullOrEmpty())
            writer.WriteString("translate", chatMessage.Translate);
        if (!chatMessage.Keybind.IsNullOrEmpty())
            writer.WriteString("keybind", chatMessage.Keybind);
        if (chatMessage.Color.HasValue)
            writer.WriteString("color", chatMessage.Color.Value.ToString());
        if (!chatMessage.Insertion.IsNullOrEmpty())
            writer.WriteString("insertion", chatMessage.Insertion);

        // Unset flags are left out so they're inherited, as vanilla's text components do.
        if (chatMessage.Bold is not null)
            writer.WriteBool("bold", chatMessage.Bold.Value);
        if (chatMessage.Italic is not null)
            writer.WriteBool("italic", chatMessage.Italic.Value);
        if (chatMessage.Underlined is not null)
            writer.WriteBool("underlined", chatMessage.Underlined.Value);
        if (chatMessage.Strikethrough is not null)
            writer.WriteBool("strikethrough", chatMessage.Strikethrough.Value);
        if (chatMessage.Obfuscated is not null)
            writer.WriteBool("obfuscated", chatMessage.Obfuscated.Value);
        if (chatMessage.ShadowColor is int shadowColor)
            writer.WriteInt("shadow_color", shadowColor);

        if (chatMessage.ClickEvent is not null)
            writer.WriteTag(chatMessage.ClickEvent.ToNbt());
        if (chatMessage.HoverEvent is not null)
            writer.WriteTag(chatMessage.HoverEvent.ToNbt());

        if (chatMessage.Extra is List<ChatMessage> extras)
        {
            writer.WriteListStart("extra", NbtTagType.Compound, extras.Count);

            foreach (var item in extras)
            {
                writer.WriteCompoundStart();

                writer.WriteChatMessage(item);

                writer.EndCompound();
            }

            writer.EndList();
        }

        if (chatMessage.With is List<ChatMessage> extraChatComponents)
        {
            var list = new NbtList(NbtTagType.Compound, "with");

            foreach (var item in extraChatComponents)
                list.Add(item.ToNbt());

            writer.WriteTag(list);
        }
    }

    public static NbtCompound ToNbt(this ChatMessage chatMessage, string name = "")
    {
        var compound = new NbtCompound(name);

        if (chatMessage.Bold is not null)
            compound.Add(new NbtTag<bool>("bold", chatMessage.Bold.Value));
        if (chatMessage.Italic is not null)
            compound.Add(new NbtTag<bool>("italic", chatMessage.Italic.Value));
        if (chatMessage.Underlined is not null)
            compound.Add(new NbtTag<bool>("underlined", chatMessage.Underlined.Value));
        if (chatMessage.Strikethrough is not null)
            compound.Add(new NbtTag<bool>("strikethrough", chatMessage.Strikethrough.Value));
        if (chatMessage.Obfuscated is not null)
            compound.Add(new NbtTag<bool>("obfuscated", chatMessage.Obfuscated.Value));
        if (chatMessage.ShadowColor is int shadowColor)
            compound.Add(new NbtTag<int>("shadow_color", shadowColor));

        if (!chatMessage.Text.IsNullOrEmpty() || chatMessage.Translate is null)
            compound.Add(new NbtTag<string>("text", chatMessage.Text ?? string.Empty));
        if (!chatMessage.Translate.IsNullOrEmpty())
            compound.Add(new NbtTag<string>("translate", chatMessage.Translate!));
        if (chatMessage.Color.HasValue)
            compound.Add(new NbtTag<string>("color", chatMessage.Color.Value.ToString()));
        if (!chatMessage.Insertion.IsNullOrEmpty())
            compound.Add(new NbtTag<string>("insertion", chatMessage.Insertion!));

        if (chatMessage.ClickEvent is not null)
            compound.Add(chatMessage.ClickEvent.ToNbt());
        if (chatMessage.HoverEvent is not null)
            compound.Add(chatMessage.HoverEvent.ToNbt());

        if (chatMessage.Extra is not null)
            compound.Add(SavedList("extra", chatMessage.Extra.Select(value => value.ToNbt())));

        if (chatMessage.With is not null)
            compound.Add(SavedList("with", chatMessage.With.Select(value => value.ToNbt())));

        return compound;
    }

    public static ChatMessage FromNbt(this ChatMessage chatMessage, NbtCompound root)
    {
        chatMessage.Text = root.TryGetTagValue<string>("text", out var text) ? text : null;
        chatMessage.Translate = root.TryGetTagValue<string>("translate", out var translate) ? translate : null;

        if (root.TryGetTagValue<string>("color", out var color))
            chatMessage.Color = new HexColor(color);

        if (root.TryGetTagValue<string>("insertion", out var insertion))
            chatMessage.Insertion = insertion;

        chatMessage.Bold = root.TryGetBool("bold", out var bold) ? bold : null;
        chatMessage.Italic = root.TryGetBool("italic", out var italic) ? italic : null;
        chatMessage.Underlined = root.TryGetBool("underlined", out var underlined) ? underlined : null;
        chatMessage.Strikethrough = root.TryGetBool("strikethrough", out var strikethrough) ? strikethrough : null;
        chatMessage.Obfuscated = root.TryGetBool("obfuscated", out var obfuscated) ? obfuscated : null;
        chatMessage.ShadowColor = root.TryGetTagValue<int>("shadow_color", out var shadowColor) ? shadowColor : null;

        if (root.TryGetTag<NbtCompound>("click_event", out var clickEventCompound))
            chatMessage.ClickEvent = clickEventCompound.ToClickComponent();
        if (root.TryGetTag<NbtCompound>("hover_event", out var hoverEventCompound))
            chatMessage.HoverEvent = hoverEventCompound.ToHoverComponent();

        // Nested components can be plain strings or lists as well as compounds, like the top-level one.
        if (root.TryGetTag<NbtList>("extra", out var extrasList))
            chatMessage.AddExtra(extrasList.Select(tag => tag.TextFromNbt()).OfType<ChatMessage>().ToList());

        if (root.TryGetTag("with", out var withList))
        {
            var arguments = ListElements(withList).Select(TranslationArgument).OfType<ChatMessage>().ToList();
            chatMessage.AddChatComponent(arguments);
        }

        return chatMessage;
    }

    // Vanilla's translation arguments are texts, or numbers and booleans shown as Java writes them. A saved boolean is
    // a byte, so like vanilla's it shows as a number once saved.
    private static ChatMessage? TranslationArgument(INbtTag argument)
    {
        if (argument.TextFromNbt() is ChatMessage text)
            return text;

        var written = argument switch
        {
            NbtTag<bool> flag => flag.Value ? "true" : "false",
            NbtTag<byte> number => unchecked((sbyte)number.Value).ToString(CultureInfo.InvariantCulture),
            NbtTag<short> number => number.Value.ToString(CultureInfo.InvariantCulture),
            NbtTag<int> number => number.Value.ToString(CultureInfo.InvariantCulture),
            NbtTag<long> number => number.Value.ToString(CultureInfo.InvariantCulture),
            NbtTag<float> number => JavaDecimal(number.Value),
            NbtTag<double> number => JavaDecimal(number.Value),
            _ => null
        };

        return written is null ? null : ChatMessage.Simple(written);
    }

    /// <summary>
    /// A double as Java's <c>Double.toString</c> writes it: at least one decimal, and scientific notation
    /// (<c>1.0E-5</c>) below 10^-3 and from 10^7.
    /// </summary>
    internal static string JavaDecimal(double value) => JavaDecimal(
        value.ToString(CultureInfo.InvariantCulture),
        value.ToString("E1", CultureInfo.InvariantCulture));

    /// <summary>
    /// A float as Java's <c>Float.toString</c> writes it, like <see cref="JavaDecimal(double)"/>.
    /// </summary>
    internal static string JavaDecimal(float value) => JavaDecimal(
        value.ToString(CultureInfo.InvariantCulture),
        value.ToString("E1", CultureInfo.InvariantCulture));

    // A number from the shortest digits .NET writes for it, which are Java's when there's more than one, and the two
    // closest to it, rounded from its exact value.
    private static string JavaDecimal(string shortest, string closestTwo)
    {
        if (shortest is "NaN" or "∞" or "-∞" or "Infinity" or "-Infinity")
            return shortest.Replace("∞", "Infinity");

        var negative = shortest.StartsWith('-');
        var (digits, power) = Decimal(shortest.TrimStart('-'));
        if (digits.Length == 0)
            return negative ? "-0.0" : "0.0";

        // Java writes at least two digits, and with one shortest digit picks the two closest to the number: those of a
        // subnormal can differ from it (Double.MIN_VALUE is 4.9E-324, not 5.0E-324).
        if (digits.Length == 1)
            (digits, power) = Decimal(closestTwo.TrimStart('-'));

        var sign = negative ? "-" : string.Empty;

        if (power is >= -3 and < 7)
            return sign + Plain(digits, power);

        var fraction = digits.Length > 1 ? digits[1..] : "0";

        return $"{sign}{digits[0]}.{fraction}E{power}";
    }

    // A written decimal's significant digits without trailing zeros, and the power of ten of the first; no digits for
    // zero.
    private static (string Digits, int Power) Decimal(string written)
    {
        var parts = written.Split('E');
        var mantissa = parts[0];
        var exponent = parts.Length > 1 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 0;
        var point = mantissa.IndexOf('.');
        var allDigits = mantissa.Replace(".", string.Empty);
        var significant = allDigits.TrimStart('0');
        var leadingZeros = allDigits.Length - significant.Length;
        var power = (point < 0 ? mantissa.Length : point) - 1 - leadingZeros + exponent;

        return (significant.TrimEnd('0'), power);
    }

    // Digits whose first is 10^power, written out with at least one decimal.
    private static string Plain(string digits, int power)
    {
        if (power < 0)
            return "0." + new string('0', -power - 1) + digits;

        var whole = digits.Length > power + 1 ? digits[..(power + 1)] : digits.PadRight(power + 1, '0');
        var fraction = digits.Length > power + 1 ? digits[(power + 1)..] : "0";

        return $"{whole}.{fraction}";
    }

    public static ClickComponent ToClickComponent(this NbtCompound compound)
    {
        var action = SavedEnum<ClickAction>(compound.GetString("action")!);
        var field = ClickField(action);
        return new ClickComponent
        {
            Action = action,
            Value = action == ClickAction.ChangePage
                ? compound.GetInt(field).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : compound.SavedString(field) ?? compound.SavedString("value")!
        };
    }

    public static HoverComponent? ToHoverComponent(this NbtCompound compound)
    {
        var action = compound.SavedString("action");
        if (action == "show_text" && compound.TryGetTag("value", out var text))
        {
            return new HoverComponent
            {
                Action = HoverAction.ShowText,
                Contents = new HoverChatContent { ChatMessage = text.TextFromNbt()! }
            };
        }

        if (action == "show_item" && compound.ItemFromNbt() is ItemStack item)
        {
            return new HoverComponent
            {
                Action = HoverAction.ShowItem,
                Contents = new HoverItemContent { Item = item }
            };
        }

        if (action == "show_entity")
            throw new NotSupportedException("Entity hover text cannot be represented without a live entity.");

        if (!compound.TryGetTag<NbtCompound>("contents", out var contentsCompound))
            return null;

        IHoverContent contents = default!;

        if (contentsCompound.TryGetTag<NbtCompound>("chat_message", out var chatMessageCompound))
            contents = new HoverChatContent { ChatMessage = ChatMessage.Empty.FromNbt(chatMessageCompound) };
        else if (contentsCompound.TryGetTag<NbtCompound>("item", out var itemCompound))
            contents = new HoverItemContent { Item = itemCompound.ItemFromNbt()! };
        else if (contentsCompound.TryGetTag<NbtCompound>("entity", out var entityCompound))
            throw new NotImplementedException("Entity deserialization not supported for chat message.");

        return new HoverComponent
        {
            Action = Enum.Parse<HoverAction>(compound.GetString("action")!.ToPascalCase()),
            Contents = contents
        };
    }

    public static NbtCompound ToNbt(this HoverComponent hoverComponent)
    {
        var compound = new NbtCompound("hover_event")
        {
            new NbtTag<string>("action", JsonNamingPolicy.SnakeCaseLower.ConvertName(hoverComponent.Action.ToString())),
        };

        if (hoverComponent.Contents is HoverChatContent chatContent)
        {
            compound.Add(chatContent.ChatMessage.ToNbt("value"));
        }
        else if (hoverComponent.Contents is HoverItemContent itemContent)
        {
            foreach (var (name, tag) in itemContent.Item.ToNbt())
                compound.Add(name, tag);
        }
        else if (hoverComponent.Contents is HoverEntityComponent)
        {
            throw new NotSupportedException("Entity hover text cannot be represented without a live entity.");
        }

        return compound;
    }

    private static string ClickField(ClickAction action) => action switch
    {
        ClickAction.OpenUrl => "url",
        ClickAction.ChangePage => "page",
        ClickAction.CopyToClipboard => "value",
        _ => "command"
    };

    public static NbtCompound ToNbt(this ClickComponent clickComponent)
    {
        var compound = new NbtCompound("click_event")
        {
            new NbtTag<string>("action", clickComponent.Action.ToString().ToSnakeCase())
        };
        var field = ClickField(clickComponent.Action);
        compound.Add(clickComponent.Action == ClickAction.ChangePage
            ? new NbtTag<int>(
                field, int.Parse(clickComponent.Value, System.Globalization.CultureInfo.InvariantCulture))
            : (INbtTag)new NbtTag<string>(field, clickComponent.Value));
        return compound;
    }

    public static void WriteNbtCompound(this INetStreamWriter writer, NbtCompound compound)
    {
        using var nbtWriter = new RawNbtWriter(true);

        nbtWriter.WriteEntries(compound);

        nbtWriter.EndCompound();
        nbtWriter.TryFinish();

        writer.WriteByteArray(nbtWriter.Data);
    }
}
