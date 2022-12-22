using Obsidian.SourceGenerators.Registry.Models;
using System.Text;

namespace Obsidian.SourceGenerators.Registry;
public partial class BlockGenerator
{

    private static void GenerateValueStore(CodeBuilder stateBuilder, BlockProperty[] properties)
    {
        stateBuilder.Statement("private Dictionary<string, List<string>> valueStore = new()");
        foreach (var property in properties)
        {
            stateBuilder.Indent().Append("{ ").Append($"\"{property.Name}\", new()").Append("{ ");

            foreach (var value in property.Values)
                stateBuilder.Append($"\"{value}\", ");

            stateBuilder.Append("} },").Line();
        }
        stateBuilder.EndScope(true).Line();
    }

    private static void GeneratePossibleStates(CodeBuilder stateBuilder, Dictionary<int, List<string>> stateValues, BlockProperty[] properties)
    {
        stateBuilder.Statement("private Dictionary<string, int> possibleStates = new()");
        foreach (var kv in stateValues)
        {
            var key = kv.Key;
            var values = kv.Value;

            var sb = new StringBuilder();

            foreach (var property in properties)
            {
                foreach (var value in values)
                {
                    var index = Array.FindIndex(property.Values, v => v.Equals(value, StringComparison.OrdinalIgnoreCase));

                    if (index == -1)
                        continue;

                    sb.Append(index);
                }
            }

            stateBuilder.Indent().Append("{ ").Append($"\"{sb}\", {key}").Append(" },").Line();
        }

        stateBuilder.EndScope(true);
    }

    private static void CreateStateBuilders(Block[] blocks, GeneratorExecutionContext ctx)
    {
        foreach (var block in blocks)
        {
            if (block.Properties.Length == 0)
                continue;

            var blockName = block.Name;

            var fullName = $"{blockName}StateBuilder";

            var stateBuilder = new CodeBuilder()
                .Using("System.Text")
                .Namespace("Obsidian.API.BlockStates.Builders")
                .Line()
                .Type($"public sealed class {fullName} : IStateBuilder<{blockName}State>");

            GenerateValueStore(stateBuilder, block.Properties);
            GeneratePossibleStates(stateBuilder, block.StateValues, block.Properties);

            foreach (var property in block.Properties)
            {
                var name = property.Name.Replace("Is", string.Empty);

                var setter = property.Type == "int" ? "private set;" : "set;";

                stateBuilder.Line().Indent().Append($"public {property.Type} {name} ").Append("{ get; ").Append($"{setter}").Append(" }");
            }

            stateBuilder.Line().Line().Method($"public {fullName}()").EndScope();

            stateBuilder.Line().Line().Method($"public {fullName}(IBlockState oldState)");

            stateBuilder.Line($"var state = ({blockName}State)oldState;");

            foreach (var property in block.Properties)
            {
                var oldName = property.Name;
                var name = oldName.Replace("Is", string.Empty);

                stateBuilder.Line($"this.{name} = state.{oldName};");
            }

            stateBuilder.EndScope();

            foreach (var property in block.Properties)
            {
                var name = property.Name;
                var type = property.Type;

                var prefix = name.StartsWith("Is") ? "" : $"With";
                var camelCaseName = name.ToCamelCase();

                stateBuilder.Line().Method($"public {fullName} {prefix}{name}({type} {camelCaseName}{(type == "bool" ? " = true" : "")})");

                stateBuilder.Line($"this.{name.Replace("Is", string.Empty)} = {camelCaseName};");
                stateBuilder.Line("return this;").EndScope();
            }

            stateBuilder.Line().Line().Method($"public {blockName}State Build()");

            //Gotta make sure we get the right state id
            BuildStateFinder(stateBuilder, block.Properties);

            stateBuilder.Statement("return new()");
            stateBuilder.Line("Id = stateId,");
            foreach (var property in block.Properties)
            {
                var name = property.Name;

                stateBuilder.Line($"{name} = this.{name.Replace("Is", string.Empty)},");
            }

            stateBuilder.EndScope(true).EndScope();

            stateBuilder.EndScope();

            ctx.AddSource($"{fullName}.g.cs", stateBuilder.ToString());
        }
    }

    private static void BuildStateFinder(CodeBuilder stateBuilder, BlockProperty[] properties)
    {
        stateBuilder.Line("var sb = new StringBuilder();").Line();

        foreach (var property in properties)
        {
            var name = property.Name;
            var type = property.Type;

            if (type == "int")
            {
                var maxValue = property.Values[property.Values.Length - 1];
                stateBuilder.Statement($"if(this.{name} > {maxValue})")
                    .Line($"throw new ArgumentOutOfRangeException(\"{name} must be <= {maxValue}\");")
                    .EndScope();
            }
        }

        foreach (var property in properties)
        {
            var name = property.Name;
            var sanitizedName = property.Name.Replace("Is", string.Empty);

            stateBuilder.Indent().Append($"sb.Append(Array.FindIndex(this.valueStore[\"{name}\"].ToArray(), ")
                .Append($"v => v.Equals(this.{sanitizedName}.ToString(), StringComparison.OrdinalIgnoreCase)));").Line();
        }

        stateBuilder.Line().Line($"var stateId = this.possibleStates[sb.ToString()];");
    }
}
