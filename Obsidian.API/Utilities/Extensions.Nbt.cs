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
        if (!chatMessage.Text.IsNullOrEmpty())
            writer.WriteString("text", chatMessage.Text);
        if (!chatMessage.Translate.IsNullOrEmpty())
            writer.WriteString("translate", chatMessage.Translate);
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

        if (chatMessage.ClickEvent != null)
            writer.WriteTag(chatMessage.ClickEvent.ToNbt());
        if (chatMessage.HoverEvent != null)
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

        if (!chatMessage.Text.IsNullOrEmpty())
            compound.Add(new NbtTag<string>("text", chatMessage.Text!));
        if (!chatMessage.Translate.IsNullOrEmpty())
            compound.Add(new NbtTag<string>("translate", chatMessage.Translate!));
        if (chatMessage.Color.HasValue)
            compound.Add(new NbtTag<string>("color", chatMessage.Color.Value.ToString()));
        if (!chatMessage.Insertion.IsNullOrEmpty())
            compound.Add(new NbtTag<string>("insertion", chatMessage.Insertion!));

        if (chatMessage.ClickEvent != null)
            compound.Add(chatMessage.ClickEvent.ToNbt());
        if (chatMessage.HoverEvent != null)
            compound.Add(chatMessage.HoverEvent.ToNbt());

        return compound;
    }

    public static ChatMessage FromNbt(this ChatMessage chatMessage, NbtCompound root)
    {
        if (root.TryGetTagValue<string>("text", out var text))
            chatMessage.Text = text;

        if (root.TryGetTagValue<string>("translate", out var translate))
            chatMessage.Translate = translate;

        if (root.TryGetTagValue<string>("color", out var color))
            chatMessage.Color = new HexColor(color);

        if (root.TryGetTagValue<string>("insertion", out var insertion))
            chatMessage.Insertion = insertion;

        chatMessage.Bold = root.TryGetTag("bold", out _) ? root.GetBool("bold") : null;
        chatMessage.Italic = root.TryGetTag("italic", out _) ? root.GetBool("italic") : null;
        chatMessage.Underlined = root.TryGetTag("underlined", out _) ? root.GetBool("underlined") : null;
        chatMessage.Strikethrough = root.TryGetTag("strikethrough", out _) ? root.GetBool("strikethrough") : null;
        chatMessage.Obfuscated = root.TryGetTag("obfuscated", out _) ? root.GetBool("obfuscated") : null;

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
        return new ClickComponent
        {
            Action = Enum.Parse<ClickAction>(compound.GetString("action")!.ToPascalCase()),
            Value = compound.GetString("value")!
        };
    }

    //TODO: CHat message stuff need to be updated.
    public static HoverComponent? ToHoverComponent(this NbtCompound compound)
    {
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
            compound.Add(chatContent.ChatMessage.ToNbt("value"));
        else if (hoverComponent.Contents is HoverItemContent)
            throw new NotImplementedException("Missing properties from ItemStack can't implement.");
        else if (hoverComponent.Contents is HoverEntityComponent)
            throw new NotImplementedException("Re-implement");

        return compound;
    }

    public static NbtCompound ToNbt(this ClickComponent clickComponent) => new("click_event")
    {
         new NbtTag<string>("action", JsonNamingPolicy.SnakeCaseLower.ConvertName(clickComponent.Action.ToString())),
         new NbtTag<string>("command", clickComponent.Value)
    };

    public static void WriteNbtCompound(this INetStreamWriter writer, NbtCompound compound)
    {
        using var nbtWriter = new RawNbtWriter(true);

        foreach (var (_, tag) in compound)
            nbtWriter.WriteTag(tag);

        nbtWriter.EndCompound();
        nbtWriter.TryFinish();

        writer.WriteByteArray(nbtWriter.Data);
    }
}
