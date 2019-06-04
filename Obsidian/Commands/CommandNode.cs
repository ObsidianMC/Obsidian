using Obsidian.Net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Obsidian.Commands
{
    /// <summary>
    /// https://wiki.vg/Command_Data
    /// </summary>
    public class CommandNode
    {
        public CommandNodeType Type;

        public List<int> ChildrenIndices = new List<int>();

        public List<CommandNode> Children = new List<CommandNode>();

        public string Name;

        public string Identifier;

        public async Task<byte[]> ToArrayAsync()
        {
            using (var memoryStream = new MinecraftStream(new MemoryStream()))
            {
                await memoryStream.WriteByteAsync((sbyte)Type);
                await memoryStream.WriteVarIntAsync(Children.Count);

                foreach (int index in ChildrenIndices)
                {
                    await memoryStream.WriteVarIntAsync(index);
                }

                if (Type.HasFlag(CommandNodeType.HasRedirect))
                {
                    //TODO: Add redirect functionality if needed
                    await memoryStream.WriteVarIntAsync(0);
                }

                if (Type.HasFlag(CommandNodeType.Argument) || Type.HasFlag(CommandNodeType.Literal))
                {
                    if (string.IsNullOrWhiteSpace(Name))
                    {
                        //HACK: Replace or add new type to replace this exception.
                        throw new ArgumentNullException(message: "Name of a command node is null or a whitespace.", null);
                    }

                    await memoryStream.WriteStringAsync(Name);
                }

                if (Type.HasFlag(CommandNodeType.Argument))
                {
                    await memoryStream.WriteIdentifierAsync(Identifier);
                    if (Identifier.Equals("brigadier:string", StringComparison.InvariantCultureIgnoreCase))
                    {
                        //HACK: for debug, first test, please remove if better arguments have been aded.
                        await memoryStream.WriteVarIntAsync(1);
                    }
                }

                return memoryStream.ToArray();
            }
        }
    }
}
