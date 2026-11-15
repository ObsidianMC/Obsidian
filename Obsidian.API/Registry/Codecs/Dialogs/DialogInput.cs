using Obsidian.Nbt;
using Obsidian.Nbt.Interfaces;

namespace Obsidian.API.Registry.Codecs.Dialogs;

/// <summary>Defines one selectable value of a single-option dialog input.</summary>
/// <param name="Id">The value submitted when this option is selected.</param>
/// <param name="Display">The optional display text for the option.</param>
/// <param name="Initial">Whether this option is initially selected.</param>
public sealed record DialogOption(string Id, ChatMessage? Display = null, bool Initial = false);

/// <summary>
/// Defines a typed client dialog input and its serialized settings.
/// </summary>
public sealed class DialogInput : INbtSerializable
{
    /// <summary>
    /// Gets the input key, containing only ASCII letters, digits and underscores.
    /// </summary>
    public string Key { get; }
    private readonly string type;
    private readonly ChatMessage label;
    private readonly Action<INbtWriter> write;

    private DialogInput(string key, string type, ChatMessage label, Action<INbtWriter> write)
    {
        if (string.IsNullOrEmpty(key) || key.Any(c => !(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_')))
            throw new ArgumentException("Input keys may contain only letters, digits and underscores.", nameof(key));
        Key = key;
        this.type = type;
        this.label = label;
        this.write = write;
    }

    /// <summary>
    /// Writes the input type, key, label and type-specific settings to NBT.
    /// </summary>
    public void Write(INbtWriter writer)
    {
        writer.WriteString("type", type);
        writer.WriteString("key", Key);
        DialogValidation.WriteText(writer, "label", label);
        write(writer);
    }

    /// <summary>
    /// Creates a text input with positive maximum length and optional multiline limits; widths and heights use client
    /// pixels.
    /// </summary>
    public static DialogInput Text(string key, ChatMessage label, int maxLength = 32, string initial = "",
        int width = 200, bool labelVisible = true, bool multiline = false, int? maxLines = null, int? height = null)
    {
        DialogValidation.Range(maxLength, 1, int.MaxValue, nameof(maxLength));
        DialogValidation.Range(width, 1, 1024, nameof(width));
        if (initial.Length > maxLength) throw new ArgumentException("Initial text exceeds maxLength.", nameof(initial));
        if (maxLines.HasValue) DialogValidation.Range(maxLines.Value, 1, int.MaxValue, nameof(maxLines));
        if (height.HasValue) DialogValidation.Range(height.Value, 1, 512, nameof(height));
        return new(key, "minecraft:text", label, writer =>
        {
            writer.WriteInt("width", width);
            writer.WriteBool("label_visible", labelVisible);
            writer.WriteString("initial", initial);
            writer.WriteInt("max_length", maxLength);
            if (multiline || maxLines.HasValue || height.HasValue)
            {
                writer.WriteCompoundStart("multiline");
                if (maxLines.HasValue) writer.WriteInt("max_lines", maxLines.Value);
                if (height.HasValue) writer.WriteInt("height", height.Value);
                writer.EndCompound();
            }
        });
    }

    /// <summary>
    /// Creates a Boolean input with explicit submitted strings for its true and false states.
    /// </summary>
    public static DialogInput Boolean(string key, ChatMessage label, bool initial = false,
        string onTrue = "true", string onFalse = "false") => new(key, "minecraft:boolean", label, writer =>
    {
        writer.WriteBool("initial", initial);
        writer.WriteString("on_true", onTrue);
        writer.WriteString("on_false", onFalse);
    });

    /// <summary>
    /// Creates a selector with nonempty unique options and at most one initial selection.
    /// </summary>
    public static DialogInput SingleOption(string key, ChatMessage label, IEnumerable<DialogOption> options,
        int width = 200, bool labelVisible = true)
    {
        var entries = options.ToArray();
        if (entries.Length == 0 || entries.Count(x => x.Initial) > 1 || entries.Select(x => x.Id).Distinct().Count() != entries.Length)
            throw new ArgumentException("Options must be non-empty, unique and have at most one initial selection.", nameof(options));
        DialogValidation.Range(width, 1, 1024, nameof(width));
        return new(key, "minecraft:single_option", label, writer =>
        {
            writer.WriteInt("width", width);
            writer.WriteBool("label_visible", labelVisible);
            writer.WriteListStart("options", NbtTagType.Compound, entries.Length);
            foreach (var option in entries)
            {
                writer.WriteCompoundStart();
                writer.WriteString("id", option.Id);
                if (option.Display is not null) DialogValidation.WriteText(writer, "display", option.Display);
                writer.WriteBool("initial", option.Initial);
                writer.EndCompound();
            }
            writer.EndList();
        });
    }

    /// <summary>
    /// Creates a finite increasing numeric slider with an optional positive step and an initial value inside its range.
    /// </summary>
    public static DialogInput NumberRange(string key, ChatMessage label, float start, float end,
        float? initial = null, float? step = null, int width = 200, string labelFormat = "options.generic_value")
    {
        var value = initial ?? (start / 2 + end / 2);
        if (!float.IsFinite(start) || !float.IsFinite(end) || start >= end || !float.IsFinite(value) || value < start || value > end)
            throw new ArgumentException("Expected a finite increasing range with an initial value inside it.");
        if (step.HasValue && (!float.IsFinite(step.Value) || step.Value <= 0))
            throw new ArgumentOutOfRangeException(nameof(step));
        DialogValidation.Range(width, 1, 1024, nameof(width));
        return new(key, "minecraft:number_range", label, writer =>
        {
            writer.WriteInt("width", width);
            writer.WriteString("label_format", labelFormat);
            writer.WriteFloat("start", start);
            writer.WriteFloat("end", end);
            writer.WriteFloat("initial", value);
            if (step.HasValue) writer.WriteFloat("step", step.Value);
        });
    }
}
