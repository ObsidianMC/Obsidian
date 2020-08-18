using Obsidian.Net;
using Obsidian.Net.Packets.Play;
using Obsidian.Util.Extensions;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Obsidian.Commands
{
    /// <summary>
    /// https://wiki.vg/Command_Data
    /// </summary>
    public class CommandNode
    {
        public CommandParser Parser { get; set; }

        public CommandNodeType Type { get; set; }

        public List<CommandNode> Children = new List<CommandNode>();

        public string Name { get; set; } = string.Empty;

        public int Index { get; set; }

        public async Task CopyToAsync(MinecraftStream stream)
        {
            await using var dataStream = new MinecraftStream();
            await dataStream.WriteByteAsync((sbyte)this.Type);
            await dataStream.WriteVarIntAsync(this.Children.Count);

            foreach (CommandNode childNode in this.Children)
            {
                 await dataStream.WriteVarIntAsync(childNode.Index);
            }

            if (this.Type.HasFlag(CommandNodeType.HasRedirect))
            {
                //TODO: Add redirect functionality if needed
                await dataStream.WriteVarIntAsync(0);
            }

            if ((this.Type.HasFlag(CommandNodeType.Argument) || this.Type.HasFlag(CommandNodeType.Literal)) && !this.Name.IsNullOrEmpty())
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

        public async Task<CommandNode> ReadFromAsync(MinecraftStream dataStream)
        {
            var type = (CommandNodeType)await dataStream.ReadByteAsync();
            var childrenCount = await dataStream.ReadVarIntAsync();

            for (int i = 0; i < childrenCount; i++)
            {
                await dataStream.ReadVarIntAsync();
            }

            if (type.HasFlag(CommandNodeType.HasRedirect))
                await dataStream.ReadVarIntAsync();

            if (type.HasFlag(CommandNodeType.Argument) || type.HasFlag(CommandNodeType.Literal))
            {
                var name = await dataStream.ReadStringAsync();
                this.Name = name;
            }

            if (type.HasFlag(CommandNodeType.Argument))
            {
                this.Parser = new CommandParser(await dataStream.ReadStringAsync());
            }

            return this;
        }

        public void AddChild(CommandNode child) => this.Children.Add(child);

    }
}