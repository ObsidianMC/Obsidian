namespace Obsidian.Commands;

/// <summary>
/// https://wiki.vg/Command_Data
/// </summary>
public class CommandNode
{
    public string? Name { get; set; }

    public int Index { get; set; }

    public BaseArgumentParser? Parser { get; set; }

    public CommandNodeType Type { get; set; }

    public List<CommandNode> Children = [];

    public string? SuggestionType { get; set; }

    public void CopyTo(INetStreamWriter writer)
    {
        writer.WriteByte((sbyte)Type);

        writer.WriteLengthPrefixedArray(writer.WriteVarInt, Children.Select(c => c.Index).ToArray());

        if (Type.HasFlag(CommandNodeType.HasRedirect))
        {
            writer.WriteVarInt(Index);
        }

        if (Type.HasFlag(CommandNodeType.Literal) || Type.HasFlag(CommandNodeType.Argument))
        {
            writer.WriteString(Name!);
        }

        if (Type.HasFlag(CommandNodeType.Argument))
        {
            Parser!.Write(writer);
        }

        if (Type.HasFlag(CommandNodeType.HasSuggestions))
        {
            writer.WriteString(SuggestionType!);
        }
    }

    public void AddChild(CommandNode child) => Children.Add(child);
}
