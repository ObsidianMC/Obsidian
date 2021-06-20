using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry
{
    internal sealed class BlocksParser : AssetsParser
    {
        public override string SourceFile => "blocks.json";
        public override string Name => "Blocks";

        public override void ParseAsset(GeneratorExecutionContext context, string asset)
        {
            BlockHandler blockHandler = context.Compilation.AssemblyName switch
            {
                "Obsidian" => new RegistryBuilder(),
                "Obsidian.API" => new BlockBuilder(),
                _ => null
            };

            if (blockHandler is null)
                return;

            using var document = JsonDocument.Parse(asset);

            foreach (JsonProperty block in document.RootElement.EnumerateObject())
            {
                blockHandler.HandleBlock(context, block);
            }

            blockHandler.Complete(context);
        }

        private abstract class BlockHandler
        {
            public abstract void HandleBlock(GeneratorExecutionContext context, JsonProperty block);
            public abstract void Complete(GeneratorExecutionContext context);
        }

        private sealed class BlockBuilder : BlockHandler
        {
            private PropertiesCollection properties = new();

            public override void HandleBlock(GeneratorExecutionContext context, JsonProperty block)
            {
                string name = block.Name.RemoveNamespace().ToPascalCase();

                int baseId = int.MaxValue;
                int defaultId = 0;
                foreach (JsonElement state in block.Value.GetProperty("states").EnumerateArray())
                {
                    int id = state.GetProperty("id").GetInt32();

                    if (id < baseId)
                        baseId = id;

                    if (state.TryGetProperty("default", out JsonElement _default) && _default.GetBoolean())
                        defaultId = id;
                }

                var blockProperties = new List<Property>();
                if (block.Value.TryGetProperty("properties", out JsonElement _properties))
                {
                    foreach (JsonProperty property in _properties.EnumerateObject())
                    {
                        blockProperties.Add(properties.GetOrAdd(property));
                    }
                }

                var builder = new CodeBuilder();
                builder.Using("System");

                builder.Line();
                builder.Namespace("Obsidian.API.Blocks");
                builder.Type($"public struct {name}");

                foreach (var property in blockProperties)
                {
                    builder.Line($"public {property.Type} {property.Name} {{ get; set; }}");
                }

                builder.Line();
                builder.Line("private int state;");

                builder.Line();
                builder.Line("#region Constants");
                builder.Line($"internal const int BaseId = {baseId};");
                builder.Line($"internal const int DefaultId = {defaultId};");
                builder.Line("#endregion");

                builder.Line();
                builder.Method($"internal {name}(int state)");
                builder.Line("this.state = state;");
                foreach (var property in blockProperties)
                {
                    builder.Line($"{property.Name} = default;");
                }
                builder.EndScope();

                builder.Line();
                builder.Method($"public static implicit operator Block({name} block)");
                builder.Line("return new Block(BaseId, block.state);");
                builder.EndScope();

                builder.Line();
                builder.Method($"public static explicit operator {name}(Block block)");
                builder.Statement("if (block.BaseId == BaseId)");
                builder.Line($"return new {name}(block.StateId - BaseId);");
                builder.EndScope();
                builder.Line($"throw new InvalidCastException($\"Cannot cast {{block.Name}} to {name}\");");
                builder.EndScope();

                builder.EndScope();
                builder.EndScope();

                context.AddSource($"{name}.cs", builder);
            }

            public override void Complete(GeneratorExecutionContext context)
            {
                properties.EmitEnums(context);
            }
        }

        private sealed class RegistryBuilder : BlockHandler
        {
            private CodeBuilder blockRegistry;
            private StringBuilder stateToMatch;
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

                stateToMatch = new();
                stateToMatch.Append("internal static readonly MatchTarget[] StateToMatch = { ");

                blockNames = new();
                blockNames.Append("internal static readonly string[] Names = { ");

                numericToBase = new();
                numericToBase.Append("internal static readonly short[] NumericToBase = { ");
            }

            public override void HandleBlock(GeneratorExecutionContext context, JsonProperty block)
            {
                string name = block.Name.RemoveNamespace().ToTitleCase();
                blockNames.Append($"\"{name}\", ");

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
                    stateToMatch.Append($"new MatchTarget({baseId}, {numericId}), ");
                }

                numericId++;
            }

            public override void Complete(GeneratorExecutionContext context)
            {
                stateToMatch.Append("};");
                blockRegistry.Line(stateToMatch.ToString());

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
