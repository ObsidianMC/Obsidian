using Obsidian.Net;

namespace Obsidian.Commands;

/// <summary>
/// https://wiki.vg/Command_Data
/// </summary>
public class CommandNode
{
    public string? Name { get; set; }

    public int Index { get; set; }

    public CommandParser? Parser { get; set; }

    public CommandNodeType Type { get; set; }

    public HashSet<CommandNode> Children = [];

    public void CopyTo(INetStreamWriter writer)
    {
        writer.WriteByte((sbyte)Type);
        writer.WriteVarInt(Children.Count);

        foreach (var child in Children.Select(c => c.Index))
        {
            writer.WriteVarInt(child);
        }

        if (Type.HasFlag(CommandNodeType.Literal) || Type.HasFlag(CommandNodeType.Argument))
        {
            writer.WriteString(Name!);
        }

        if (Type.HasFlag(CommandNodeType.Argument))
        {
            Parser!.Write(writer);
        }
    }

    public void AddChild(CommandNode child) => Children.Add(child);
}
