using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.Dialogs;

public sealed class DialogBody : INbtSerializable
{
    private readonly Action<INbtWriter> write;
    private DialogBody(Action<INbtWriter> write) => this.write = write;
    public void Write(INbtWriter writer) => write(writer);

    public static DialogBody Message(ChatMessage contents, int width = 200)
    {
        DialogValidation.Range(width, 1, 1024, nameof(width));
        return new(writer =>
        {
            writer.WriteString("type", "minecraft:plain_message");
            DialogValidation.WriteText(writer, "contents", contents);
            writer.WriteInt("width", width);
        });
    }

    /// <summary>Components use the vanilla item-stack codec's named NBT representation.</summary>
    public static DialogBody Item(string id, int count = 1, ChatMessage? description = null,
        int width = 16, int height = 16, bool showDecorations = true, bool showTooltip = true,
        NbtCompound? components = null, int descriptionWidth = 200)
    {
        DialogValidation.Identifier(id);
        DialogValidation.Range(count, 1, 99, nameof(count));
        DialogValidation.Range(width, 1, 256, nameof(width));
        DialogValidation.Range(height, 1, 256, nameof(height));
        DialogValidation.Range(descriptionWidth, 1, 1024, nameof(descriptionWidth));
        return new(writer =>
        {
            writer.WriteString("type", "minecraft:item");
            writer.WriteCompoundStart("item");
            writer.WriteString("id", id);
            writer.WriteInt("count", count);
            if (components is not null)
            {
                writer.WriteCompoundStart("components");
                foreach (var (_, tag) in components) writer.WriteTag(tag);
                writer.EndCompound();
            }
            writer.EndCompound();
            if (description is not null)
            {
                writer.WriteCompoundStart("description");
                DialogValidation.WriteText(writer, "contents", description);
                writer.WriteInt("width", descriptionWidth);
                writer.EndCompound();
            }
            writer.WriteInt("width", width);
            writer.WriteInt("height", height);
            writer.WriteBool("show_decorations", showDecorations);
            writer.WriteBool("show_tooltip", showTooltip);
        });
    }
}
