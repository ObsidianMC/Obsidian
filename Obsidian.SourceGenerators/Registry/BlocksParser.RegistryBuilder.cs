using Microsoft.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry
{
    internal sealed partial class BlocksParser
    {
        private sealed class RegistryBuilder : BlockHandler
        {
            private CodeBuilder blockRegistry;
            private StringBuilder stateToBase;
            private StringBuilder stateToNumeric;
            private StringBuilder blockNames;
            private StringBuilder numericToBase;

            private int numericId = 0;

            public RegistryBuilder()
            {
                blockRegistry = new();
                blockRegistry.Using("Obsidian.API.Blocks");
                blockRegistry.Line();
                blockRegistry.Namespace("Obsidian.Utilities.Registry");
                blockRegistry.Type("public static class BlocksRegistry");

                stateToBase = new();
                stateToBase.Append("internal static readonly ushort[] StateToBase = { ");

                stateToNumeric = new();
                stateToNumeric.Append("internal static readonly ushort[] StateToNumeric = { ");

                blockNames = new();
                blockNames.Append("internal static readonly string[] Names = { ");

                numericToBase = new();
                numericToBase.Append("internal static readonly ushort[] NumericToBase = { ");
            }

            public override void HandleBlock(GeneratorExecutionContext context, JsonProperty block)
            {
                blockNames.Append($"\"{block.Name}\", ");

                int baseId = int.MaxValue, idCount = 0;
                foreach (JsonElement state in block.Value.GetProperty("states").EnumerateArray())
                {
                    int id = state.GetProperty("id").GetInt32();

                    if (id < baseId)
                        baseId = id;

                    idCount++;
                }

                numericToBase.Append($"{baseId}, ");

                for (int i = 0; i < idCount; i++)
                {
                    stateToBase.Append($"{baseId}, ");
                    stateToNumeric.Append($"{numericId}, ");
                }

                numericId++;
            }

            public override void Complete(GeneratorExecutionContext context)
            {
                stateToBase.Append("};");
                blockRegistry.Line(stateToBase.ToString());
                
                stateToNumeric.Append("};");
                blockRegistry.Line(stateToNumeric.ToString());

                blockNames.Append("};");
                blockRegistry.Line(blockNames.ToString());

                numericToBase.Append("};");
                blockRegistry.Line(numericToBase.ToString());

                blockRegistry.EndScope();
                blockRegistry.EndScope();

                context.AddSource("BlocksRegistry.cs", blockRegistry);
            }
        }
    }
}
