using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;
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

        if (root.TryGetTag<NbtList>("with", out var withList))
            chatMessage.AddChatComponent(withList.Select(tag => tag.TextFromNbt()).OfType<ChatMessage>().ToList());

        return chatMessage;
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
