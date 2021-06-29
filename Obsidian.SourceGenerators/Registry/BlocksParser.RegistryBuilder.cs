using Microsoft.CodeAnalysis;
using System.Collections.Generic;
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

            private Dictionary<Conjunction, (int, int)> conjunctedIds = new();

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

                (int baseId, int idCount) = GetIds(block);

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

            private (int baseId, int idCount) GetIds(JsonProperty block)
            {
                string tag = block.Name.RemoveNamespace();
                for (int i = 0; i < Conjunctions.Length; i++)
                {
                    for (int j = 0; j < Conjunctions[i].Targets.Length; j++)
                    {
                        if (tag == Conjunctions[i].Targets[j])
                        {
                            return GetConjunctionIds(Conjunctions[i], block);
                        }
                    }
                }

                return GetUniqueIds(block);
            }

            private (int baseId, int idCount) GetUniqueIds(JsonProperty block)
            {
                int baseId = int.MaxValue, idCount = 0;
                foreach (JsonElement state in block.Value.GetProperty("states").EnumerateArray())
                {
                    int id = state.GetProperty("id").GetInt32();

                    if (id < baseId)
                        baseId = id;

                    idCount++;
                }

                return (baseId, idCount);
            }

            private (int baseId, int idCount) GetConjunctionIds(Conjunction conjunction, JsonProperty block)
            {
                if (conjunctedIds.TryGetValue(conjunction, out var ids))
                {
                    return ids;
                }

                ids = GetUniqueIds(block);
                conjunctedIds.Add(conjunction, ids);
                return ids;
            }
        }
    }
}
