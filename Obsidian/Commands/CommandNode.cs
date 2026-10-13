namespace Obsidian.Commands;

/// <summary>
/// https://wiki.vg/Command_Data
/// </summary>
public class CommandNode
{
    private CommandNode? redirect;

    public string? Name { get; set; }

    /// <summary>The node's position in the packet, assigned when the tree is added to a <c>CommandsPacket</c>.</summary>
    public int Index { get; set; }

    public BaseArgumentParser? Parser { get; set; }

    public CommandNodeType Type { get; set; }

    public List<CommandNode> Children = [];

    /// <summary>
    /// The node parsing continues from after this one, as vanilla's command aliases do. Setting it sets
    /// <see cref="CommandNodeType.HasRedirect"/>.
    /// </summary>
    public CommandNode? Redirect
    {
        get => this.redirect;
        set
        {
            this.redirect = value;
            this.Type = value is null ? this.Type & ~CommandNodeType.HasRedirect : this.Type | CommandNodeType.HasRedirect;
        }
    }

    public string? SuggestionType { get; set; }

    /// <summary>
    /// Identifies what an argument node reads, so that overloads whose leading parameters read the same share nodes.
    /// </summary>
    internal object? MergeKey { get; set; }

    public void CopyTo(INetStreamWriter writer)
    {
        writer.WriteByte((sbyte)Type);

        writer.WriteLengthPrefixedArray(writer.WriteVarInt, Children.Select(c => c.Index).ToArray());

        if (Type.HasFlag(CommandNodeType.HasRedirect))
        {
            writer.WriteVarInt(Redirect!.Index);
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
