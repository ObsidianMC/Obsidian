using Obsidian.Net;

namespace Obsidian.Commands;

/// <summary>
/// https://wiki.vg/Command_Data
/// </summary>
public class CommandNode
{
    public string Name { get; set; }

    public int Index { get; set; }

    public CommandParser Parser { get; set; }

    public CommandNodeType Type { get; set; }

    public HashSet<CommandNode> Children = new();

    public async Task CopyToAsync(MinecraftStream stream)
    {
        await using var dataStream = new MinecraftStream();
        await dataStream.WriteByteAsync((sbyte)this.Type);
        await dataStream.WriteVarIntAsync(this.Children.Count);

        foreach (var childNode in this.Children.Select(x => x.Index))
        {
            await dataStream.WriteVarIntAsync(childNode);
        }

        //if (this.Type.HasFlag(CommandNodeType.HasRedirect))
        //{
        //    //TODO: Add redirect functionality if needed
        //    await dataStream.WriteVarIntAsync(0);
        //}

        if ((this.Type.HasFlag(CommandNodeType.Argument) || this.Type.HasFlag(CommandNodeType.Literal)))
        {
            await dataStream.WriteStringAsync(this.Name);
        }

        if (this.Type.HasFlag(CommandNodeType.Argument))
        {
            await this.Parser.WriteAsync(dataStream);
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
            dataStream.WriteString(Name);
        }

        if (Type.HasFlag(CommandNodeType.Argument))
        {
            Parser.Write(dataStream);
        }

        dataStream.Position = 0;
        dataStream.CopyTo(stream);
    }

    public void AddChild(CommandNode child) => this.Children.Add(child);
}
