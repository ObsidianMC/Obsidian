using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.Dialogs;

/// <summary>A button's click behavior. Custom actions arrive as DialogActionEventArgs.</summary>
public sealed class DialogClickAction : INbtSerializable
{
    private readonly Action<INbtWriter> write;
    private DialogClickAction(Action<INbtWriter> write) => this.write = write;
    public void Write(INbtWriter writer) => write(writer);

    private static DialogClickAction Text(string type, string field, string value) => new(writer =>
    {
        writer.WriteString("type", type);
        writer.WriteString(field, value);
    });

    public static DialogClickAction RunCommand(string command) => Text("minecraft:run_command", "command", command);
    public static DialogClickAction SuggestCommand(string command) => Text("minecraft:suggest_command", "command", command);
    public static DialogClickAction CopyToClipboard(string value) => Text("minecraft:copy_to_clipboard", "value", value);
    public static DialogClickAction OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            throw new ArgumentException("Expected an HTTP or HTTPS URL.", nameof(url));
        return Text("minecraft:open_url", "url", url);
    }
    public static DialogClickAction ChangePage(int page)
    {
        DialogValidation.Range(page, 1, int.MaxValue, nameof(page));
        return new(writer => { writer.WriteString("type", "minecraft:change_page"); writer.WriteInt("page", page); });
    }
    public static DialogClickAction ShowDialog(string id)
    {
        DialogValidation.Identifier(id);
        return Text("minecraft:show_dialog", "dialog", id);
    }
    public static DialogClickAction ShowDialog(DialogElement dialog) => new(writer =>
    {
        writer.WriteString("type", "minecraft:show_dialog");
        writer.WriteCompoundStart("dialog");
        dialog.Write(writer);
        writer.EndCompound();
    });
    public static DialogClickAction CommandTemplate(string template) => Text("minecraft:dynamic/run_command", "template", template);
    public static DialogClickAction Custom(string id, INbtTag? payload = null) => CustomAction("minecraft:custom", id, "payload", payload);
    public static DialogClickAction Submit(string id, NbtCompound? additions = null) => CustomAction("minecraft:dynamic/custom", id, "additions", additions);

    private static DialogClickAction CustomAction(string type, string id, string field, INbtTag? payload)
    {
        DialogValidation.Identifier(id);
        return new(writer =>
        {
            writer.WriteString("type", type);
            writer.WriteString("id", id);
            if (payload is not null)
            {
                WritePayload(writer, field, payload);
            }
        });
    }

    private static void WritePayload(INbtWriter writer, string name, INbtTag payload)
    {
        switch (payload)
        {
            case NbtCompound compound:
                writer.WriteCompoundStart(name);
                foreach (var (_, tag) in compound) writer.WriteTag(tag);
                writer.EndCompound();
                break;
            case NbtList list:
                writer.WriteListStart(name, list.ListType, list.Count);
                foreach (var tag in list) writer.WriteListTag(tag);
                writer.EndList();
                break;
            case NbtTag<bool> tag: writer.WriteBool(name, tag.Value); break;
            case NbtTag<byte> tag: writer.WriteByte(name, tag.Value); break;
            case NbtTag<short> tag: writer.WriteShort(name, tag.Value); break;
            case NbtTag<int> tag: writer.WriteInt(name, tag.Value); break;
            case NbtTag<long> tag: writer.WriteLong(name, tag.Value); break;
            case NbtTag<float> tag: writer.WriteFloat(name, tag.Value); break;
            case NbtTag<double> tag: writer.WriteDouble(name, tag.Value); break;
            case NbtTag<string> tag: writer.WriteString(name, tag.Value!); break;
            case NbtArray<byte> tag: writer.WriteArray(name, tag.GetArray()); break;
            case NbtArray<int> tag: writer.WriteArray(name, tag.GetArray()); break;
            case NbtArray<long> tag: writer.WriteArray(name, tag.GetArray()); break;
            default: throw new ArgumentException("Unsupported custom action payload.", nameof(payload));
        }
    }
}
