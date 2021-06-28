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
            private Conjunction[] conjunctions;

            public BlockBuilder()
            {
                var blockColor = new Property("Color", "block_color", "BlockColor", new[] { "White", "Orange", "Magenta", "LightBlue", "Yellow", "Lime", "Pink", "Gray", "LightGray", "Cyan", "Purple", "Blue", "Brown", "Green", "Red", "Black" });
                var woodType = new Property("WoodType", "wood_type", "WoodType", new[] { "Oak", "Spruce", "Birch", "Jungle", "Acacia", "DarkOak" });
                var anvilDamage = new Property("Damage", "anvil_damage", "AnvilDamage", new[] { "None", "Chipped", "Damaged" });
                var infested = new Property("IsInfested", "infested", Property.BoolType, new[] { "true", "false" }, customOffset: 6);
                conjunctions = new Conjunction[]
                {
                    new Conjunction("Bed", blockColor, ColorizeTag("bed")),
                    new Conjunction("Banner", blockColor, ColorizeTag("banner")),
                    new Conjunction("WallBanner", blockColor, ColorizeTag("wall_banner")),
                    new Conjunction("Concrete", blockColor, ColorizeTag("concrete")),
                    new Conjunction("DyedShulkerBox", blockColor, ColorizeTag("shulker_box")),
                    new Conjunction("ConcretePowder", blockColor, ColorizeTag("concrete_powder")),
                    new Conjunction("StainedGlass", blockColor, ColorizeTag("stained_glass")),
                    new Conjunction("StainedGlassPane", blockColor, ColorizeTag("stained_glass_pane")),
                    new Conjunction("DyedTerracotta", blockColor, ColorizeTag("terracotta")),
                    new Conjunction("GlazedTerracotta", blockColor, ColorizeTag("glazed_terracotta")),
                    new Conjunction("Wool", blockColor, ColorizeTag("wool")),
                    new Conjunction("Carpet", blockColor, ColorizeTag("carpet")),
                    new Conjunction("Anvil", anvilDamage, "anvil", "chipped_anvil", "damaged_anvil"),
                    new Conjunction("Leaves", woodType, WoodifyTag("leaves")),
                    new Conjunction("Sapling", woodType, WoodifyTag("sapling")),
                    new Conjunction("WoodenButton", woodType, WoodifyTag("button")),
                    new Conjunction("WoodenPlanks", woodType, WoodifyTag("planks")),
                    new Conjunction("WoodenDoor", woodType, WoodifyTag("door")),
                    new Conjunction("WoodenFence", woodType, WoodifyTag("fence")),
                    new Conjunction("WoodenFenceGate", woodType, WoodifyTag("fence_gate")),
                    new Conjunction("WoodenPressurePlate", woodType, WoodifyTag("pressure_plate")),
                    new Conjunction("WoodenSign", woodType, WoodifyTag("sign")),
                    new Conjunction("WoodenWallSign", woodType, WoodifyTag("wall_sign")),
                    new Conjunction("WoodenSlab", woodType, WoodifyTag("slab")),
                    new Conjunction("WoodenStairs", woodType, WoodifyTag("stairs")),
                    new Conjunction("WoodenTrapdoor", woodType, WoodifyTag("trapdoor")),
                    new Conjunction("Wood", woodType, WoodifyTag("wood")),
                    new Conjunction("WoodLog", woodType, WoodifyTag("log")),
                    new Conjunction("StrippedWood", woodType, StripWoodifyTag("wood")),
                    new Conjunction("StrippedWoodLog", woodType, StripWoodifyTag("log")),
                    new Conjunction("Stone", infested, InfestifyTag("stone")),
                    new Conjunction("StoneBricks", infested, InfestifyTag("stone_bricks")),
                    new Conjunction("MossyStoneBricks", infested, InfestifyTag("mossy_stone_bricks")),
                    new Conjunction("CrackedStoneBricks", infested, InfestifyTag("cracked_stone_bricks")),
                    new Conjunction("ChiseledStoneBricks", infested, InfestifyTag("chiseled_stone_bricks")),
                    new Conjunction("Cobblestone", infested, InfestifyTag("cobblestone")),
                };
            }

            private string[] ColorizeTag(string tag)
            {
                return new[] { $"white_{tag}", $"orange_{tag}", $"magenta_{tag}", $"light_blue_{tag}", $"yellow_{tag}", $"lime_{tag}", $"pink_{tag}", $"gray_{tag}", $"light_gray_{tag}", $"cyan_{tag}", $"purple_{tag}", $"blue_{tag}", $"brown_{tag}", $"green_{tag}", $"red_{tag}", $"black_{tag}" };
            }

            private string[] WoodifyTag(string tag)
            {
                return new[] { $"oak_{tag}", $"spruce_{tag}", $"birch_{tag}", $"jungle_{tag}", $"acacia_{tag}", $"dark_oak_{tag}" };
            }

            private string[] StripWoodifyTag(string tag)
            {
                return new[] { $"stripped_oak_{tag}", $"stripped_spruce_{tag}", $"stripped_birch_{tag}", $"stripped_jungle_{tag}", $"stripped_acacia_{tag}", $"stripped_dark_oak_{tag}" };
            }

            private string[] InfestifyTag(string tag)
            {
                return new[] { tag, $"infested_{tag}" };
            }

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
                foreach (Conjunction conjunction in conjunctions)
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

                for (int i = 0; i < conjunctions.Length; i++)
                {
                    Conjunction conjunction = conjunctions[i];
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

            // Combination of multiple blocks as one, e.g. multiple colors of wool
            private sealed class Conjunction
            {
                public string Name { get; }
                public Property Property { get; } // Property that differentiates between actual blocks
                public string[] Targets { get; }
                public bool Emitted { get; set; }

                public Conjunction(string name, Property property, params string[] targets)
                {
                    Name = name;
                    Property = property;
                    Targets = targets;
                }
            }
        }
    }
}
