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
        internal DeclareCommands Owner { get; set; }

        internal CommandParser Parser { get; set; }

        public CommandNodeType Type;

        public List<CommandNode> Children = new List<CommandNode>();

        public string Name;

        public int Index => this.Owner.Nodes.IndexOf(this);

        public async Task CopyToAsync(MinecraftStream mcStream)
        {
            await using var stream = new MinecraftStream();
            await stream.WriteByteAsync((sbyte)this.Type);
            await stream.WriteVarIntAsync(this.Children.Count);

            foreach (CommandNode childNode in this.Children)
            {
                await stream.WriteVarIntAsync(childNode.Index);
            }

            if (this.Type.HasFlag(CommandNodeType.HasRedirect))
            {
                //TODO: Add redirect functionality if needed
                await stream.WriteVarIntAsync(0);
            }

            if (this.Type.HasFlag(CommandNodeType.Argument) || this.Type.HasFlag(CommandNodeType.Literal))
            {
                if (!this.Name.IsNullOrWhitespace())
                    await stream.WriteStringAsync(this.Name);
            }

            if (this.Type.HasFlag(CommandNodeType.Argument))
            {
                await this.Parser.WriteAsync(stream);
            }

            stream.Position = 0;

            await stream.CopyToAsync(mcStream);
        }
    }
}