using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry
{
    internal sealed partial class BlocksParser
    {
        private sealed class BlockBuilder : BlockHandler
        {
            private PropertiesCollection properties = new();

            // Note: some functionality depends on blocks & states ordered by their id
            public override void HandleBlock(GeneratorExecutionContext context, JsonProperty block)
            {
                if (HandleConjunction(context, block))
                    return;

                string name = block.Name.RemoveNamespace().ToPascalCase();

                (int baseId, int defaultId) = GetIds(block);

                EmitBlock(name, baseId, defaultId, GetBlockProperties(block), context);
            }

            public override void Complete(GeneratorExecutionContext context)
            {
                foreach (Conjunction conjunction in Conjunctions)
                {
                    properties.Add(conjunction.Property);
                }

                foreach (Property property in properties)
                {
                    if (property.IsEnum)
                    {
                        EmitEnum(property, context);
                    }
                }
            }

            private IEnumerable<Property> GetBlockProperties(JsonProperty block)
            {
                if (block.Value.TryGetProperty("properties", out JsonElement _properties))
                {
                    foreach (JsonProperty property in _properties.EnumerateObject())
                    {
                        yield return properties.GetOrAdd(property);
                    }
                }
            }

            private (int baseId, int defaultId) GetIds(JsonProperty block)
            {
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

                return (baseId, defaultId);
            }

            private void EmitBlock(string name, int baseId, int defaultId, IEnumerable<Property> properties, GeneratorExecutionContext context)
            {
                if (properties.Count() == 0)
                {
                    EmitSimpleBlock(name, baseId, context);
                    return;
                }

                var builder = new CodeBuilder();
                builder.Using("System");

                builder.Line();
                builder.Namespace("Obsidian.API.Blocks");
                builder.Type($"public readonly struct {name}");

                // Properties
                foreach (var property in properties)
                {
                    builder.Line($"public {property.Type} {property.Name} {{ get; }}");
                }

                builder.Line();
                builder.Line("private readonly uint state;");

                // Constants
                builder.Line();
                builder.Line("#region Constants");
                builder.Line($"internal const uint BaseId = {baseId};");
                builder.Line($"internal const uint DefaultId = {defaultId};");
                builder.Line("#endregion");

                // Constructor with state
                builder.Line();
                builder.Method($"internal {name}(uint state)");
                builder.Line("this.state = state;");
                foreach (var property in properties)
                {
                    builder.Line($"{property.Name} = default;"); // TODO put an actual value
                }
                builder.EndScope();

                // Constructor with properties
                builder.Line();
                builder.Method($"public {name}({string.Join(", ", properties.Select(p => $"{p.Type} {p.Name.ToCamelCase()}"))})");
                foreach (var property in properties)
                {
                    builder.Line($"{property.Name} = {property.Name.ToCamelCase()};");
                }
                int offset = 1;
                builder.Line($"state = (uint)({string.Join(" + ", properties.Reverse().Select(p => p.GetValue(ref offset)))});");
                builder.EndScope();

                // Implicit cast to Block
                builder.Line();
                builder.Method($"public static implicit operator Block({name} block)");
                builder.Line("return new Block(BaseId, block.state);");
                builder.EndScope();

                // Explicit cast from Block
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

            private void EmitSimpleBlock(string name, int baseId, GeneratorExecutionContext context)
            {
                var builder = new CodeBuilder();
                builder.Using("System");

                builder.Line();
                builder.Namespace("Obsidian.API.Blocks");
                builder.Type($"public readonly struct {name}");

                // Constants
                builder.Line();
                builder.Line("#region Constants");
                builder.Line($"internal const uint BaseId = {baseId};");
                builder.Line($"internal const uint DefaultId = {baseId};");
                builder.Line("#endregion");

                // Implicit cast to Block
                builder.Line();
                builder.Method($"public static implicit operator Block({name} block)");
                builder.Line("return new Block(BaseId, 0);");
                builder.EndScope();

                // Explicit cast from Block
                builder.Line();
                builder.Method($"public static explicit operator {name}(Block block)");
                builder.Statement("if (block.BaseId == BaseId)");
                builder.Line($"return new {name}();");
                builder.EndScope();
                builder.Line($"throw new InvalidCastException($\"Cannot cast {{block.Name}} to {name}\");");
                builder.EndScope();

                builder.EndScope();
                builder.EndScope();

                context.AddSource($"{name}.cs", builder);
            }

            private void EmitEnum(Property enumProperty, GeneratorExecutionContext context)
            {
                var builder = new CodeBuilder();

                builder.Namespace(context.Compilation.AssemblyName);
                builder.Type($"public enum {enumProperty.Type} : byte");

                foreach (string value in enumProperty.Values)
                {
                    builder.Line($"{value},");
                }

                builder.EndScope();
                builder.EndScope();

                context.AddSource($"{enumProperty.Type}.cs", builder);
            }

            private bool HandleConjunction(GeneratorExecutionContext context, JsonProperty block)
            {
                string tag = block.Name.RemoveNamespace();

                for (int i = 0; i < Conjunctions.Length; i++)
                {
                    Conjunction conjunction = Conjunctions[i];
                    for (int j = 0; j < conjunction.Targets.Length; j++)
                    {
                        if (tag == conjunction.Targets[j])
                        {
                            if (!conjunction.Emitted)
                            {
                                (int baseId, int defaultId) = GetIds(block);
                                EmitBlock(conjunction.Name, baseId, defaultId, GetBlockProperties(block).Prepend(conjunction.Property), context);
                                conjunction.Emitted = true;
                            }
                            return true;
                        }
                    }
                }

                return false;
            }
        }
    }
}
