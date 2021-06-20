using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry
{
    internal sealed class PropertiesCollection
    {
        private Dictionary<string, Property> enumProperties = new();

        private static HashSet<string> takenTypes = new();
        private static List<SpecialCase> specialCases = new();

        static PropertiesCollection()
        {
            foreach (var type in typeof(int).Assembly.GetTypes())
            {
                takenTypes.Add(type.Name);
            }
            takenTypes.Add("Half"); // Missing from netstandard 2.0

            specialCases.Add(new SpecialCase("HorizontalAxis", "x", "z")); // vs. Axis(x,y,z)
            specialCases.Add(new SpecialCase("BlockFace", "north", "east", "south", "west", "up", "down")); // vs. Facing(north, east, south, west)
            specialCases.Add(new SpecialCase("RailShape", "north_south", "east_west", "ascending_east", "ascending_west", "ascending_north", "ascending_south", "south_east", "south_west", "north_west", "north_east"));
            specialCases.Add(new SpecialCase("SpecialRailShape", "north_south", "east_west", "ascending_east", "ascending_west", "ascending_north", "ascending_south"));
            specialCases.Add(new SpecialCase("StairsShape", "straight", "inner_left", "inner_right", "outer_left", "outer_right"));
            specialCases.Add(new SpecialCase("SlabType", "top", "bottom", "double"));
            specialCases.Add(new SpecialCase("PistonType", "normal", "sticky"));
            specialCases.Add(new SpecialCase("ChestType", "single", "left", "right"));
            specialCases.Add(new SpecialCase("WallConnection", "none", "low", "tall")); // for East, West, North, South
            specialCases.Add(new SpecialCase("StructureMode", "save", "load", "corner", "data"));
            specialCases.Add(new SpecialCase("ComparatorMode", "compare", "subtract"));
        }

        public Property GetOrAdd(JsonProperty property)
        {
            string name = property.Name.RemoveNamespace().ToPascalCase();
            string[] values = property.Value.EnumerateArray().Select(element => element.GetString()).ToArray();

            string tag;
            if (values.Length == 2 && values[0] is "true" or "false")
            {
                // Bool
                return new Property(name, property.Name, Property.BoolType, values);
            }
            else if (values.All(text => int.TryParse(text, out _)))
            {
                // Int
                return new Property(name, property.Name, Property.IntType, values);
            }
            else
            {
                // Enum

                tag = GetEnumTag(values);

                string type = name;

                foreach (SpecialCase specialCase in specialCases)
                {
                    if (specialCase.Matches(tag))
                    {
                        type = specialCase.Override;
                    }
                }

                if (takenTypes.Contains(type))
                    type = "E" + type;

                if (enumProperties.TryGetValue(type, out var enumProperty))
                {
                    values = enumProperty.Values;
                }
                else
                {
                    for (int i = 0; i < values.Length; i++)
                    {
                        values[i] = values[i].ToPascalCase();
                    }
                }

                enumProperty = new Property(name, tag, type, values);
                enumProperties[type] = enumProperty;

                return enumProperty;
            }
        }

        public void EmitEnums(GeneratorExecutionContext context)
        {
            foreach (var (_, property) in enumProperties)
            {
                EmitEnum(property, context);
            }
        }

        private void EmitEnum(Property enumProperty, GeneratorExecutionContext context)
        {
            var builder = new CodeBuilder();

            builder.Namespace(context.Compilation.AssemblyName);
            builder.Type($"public enum {enumProperty.Type}");

            foreach (string value in enumProperty.Values)
            {
                builder.Line($"{value},");
            }

            builder.EndScope();
            builder.EndScope();

            context.AddSource($"{enumProperty.Type}.cs", builder);
        }

        private static string GetEnumTag(string[] values)
        {
            int hash = 0;
            foreach (string value in values)
            {
                hash ^= value.GetHashCode();
            }
            return $"enum{hash}";
        }

        private sealed class SpecialCase
        {
            public string Override { get; }
            private readonly string tag;

            public SpecialCase(string @override, params string[] values)
            {
                Override = @override;
                tag = GetEnumTag(values);
            }

            public bool Matches(string tag)
            {
                return this.tag == tag;
            }
        }
    }
}
