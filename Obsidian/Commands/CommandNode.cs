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

    public async Task CopyToAsync(MinecraftStream stream)
    {
        await using var dataStream = new MinecraftStream();
        await dataStream.WriteByteAsync((sbyte)Type);
        await dataStream.WriteVarIntAsync(Children.Count);

        foreach (var childNode in Children.Select(x => x.Index))
        {
            await dataStream.WriteVarIntAsync(childNode);
        }

        //if (this.Type.HasFlag(CommandNodeType.HasRedirect))
        //{
        //    //TODO: Add redirect functionality if needed
        //    await dataStream.WriteVarIntAsync(0);
        //}

        if ((Type.HasFlag(CommandNodeType.Argument) || Type.HasFlag(CommandNodeType.Literal)))
        {
            await dataStream.WriteStringAsync(Name!);
        }

        if (Type.HasFlag(CommandNodeType.Argument))
        {
            await Parser!.WriteAsync(dataStream);
        }

        dataStream.Position = 0;
        await dataStream.CopyToAsync(stream);
    }

    public void CopyTo(MinecraftStream stream)
    {
        using var dataStream = new MinecraftStream();
        dataStream.WriteByte((sbyte)Type);
        dataStream.WriteVarInt(Children.Count);

        foreach (var child in Children.Select(c => c.Index))
        {
            dataStream.WriteVarInt(child);
        }

        if (Type.HasFlag(CommandNodeType.Literal) || Type.HasFlag(CommandNodeType.Argument))
        {
            dataStream.WriteString(Name!);
        }

        if (Type.HasFlag(CommandNodeType.Argument))
        {
            Parser!.Write(dataStream);
        }

        dataStream.Position = 0;
        dataStream.CopyTo(stream);
    }

    public void AddChild(CommandNode child) => Children.Add(child);
}
