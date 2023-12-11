using Obsidian.SourceGenerators.Registry.Models;

namespace Obsidian.SourceGenerators.Registry;
public partial class BlocksGenerator
{
    private static void GenerateValueStore(CodeBuilder stateBuilder, BlockProperty[] properties)
    {
        stateBuilder.Statement("private Dictionary<string, string[]> valueStore = new()");
        foreach (var property in properties)
        {
            stateBuilder.Indent().Append("{ ").Append($"\"{property.Name}\", new[]").Append("{ ");

            foreach (var value in property.Values)
                stateBuilder.Append($"\"{value}\", ");

            stateBuilder.Append("} },").Line();
        }
        stateBuilder.EndScope(true).Line();
    }

    private static void GeneratePossibleStates(CodeBuilder stateBuilder, Dictionary<int, List<string>> stateValues, BlockProperty[] properties)
    {
        stateBuilder.Statement("private Dictionary<string, int> possibleStates = new()");

        var list = new List<string>();
        foreach (var kv in stateValues)
        {
            var key = kv.Key;
            var values = kv.Value;

            list.Clear();

            var count = 0;
            foreach (var value in values)
            {
                var property = properties[count++];

                var propertyValue = bool.TryParse(value, out _) ? value.ToLower() : value;

                if (!property.Values.Contains(propertyValue))
                    continue;

                var index = GetPropertyIndex(property.Values, value);

                list.Add(index);
            }

            stateBuilder.Indent().Append("{ ").Append($"\"{string.Join("-", list)}\", {key}").Append(" },").Line();
        }

        stateBuilder.EndScope(true);
    }

    private static string GetPropertyIndex(string[] array, string value)
    {
        var propertyValue = bool.TryParse(value, out _) ? value.ToLower() : value;

        if (!array.Contains(propertyValue))
            throw new ArgumentException("Failed to find value from the supplied array.", nameof(value));

        for (int i = 0; i < array.Length; i++)
        {
            if (array[i] == propertyValue)
                return $"{i}";
        }

        throw new InvalidOperationException();
    }

    private static void SetStateFromIdMethod(string fullName, CodeBuilder stateBuilder, BlockProperty[] properties)
    {
        stateBuilder.Line().Line().Method($"public {fullName}(int currentStateId)");

        stateBuilder.Line("var (key, _) = this.possibleStates.First(x => x.Value == currentStateId);");

        stateBuilder.Line("var values = key.GetStateValues(this.valueStore);");

        var count = 0;
        foreach (var property in properties)
        {
            var name = property.Name.Replace("Is", string.Empty);

            var method = property.Type switch
            {
                "bool" => $"bool.Parse(values[{count}]);",
                "int" => $"int.Parse(values[{count}]);",
                _ => $"Enum.Parse<{property.Type}>(values[{count}]);"
            };

            stateBuilder.Line($"this.{name} = {method}");

            count++;
        }

        stateBuilder.EndScope();
    }

    private static void CreateStateBuilders(Block[] blocks, SourceProductionContext ctx)
    {
        foreach (var block in blocks)
        {
            if (block.Properties.Length == 0)
                continue;

            var blockName = block.Name;

            //TODO THIS NEEDS TO BE MOVED SOMEWHERE
            if (blockName == "TrialSpawner")
                blockName = "TrialSpawnerBlock";

            var fullName = $"{blockName}StateBuilder";

            var stateBuilder = new CodeBuilder()
                .Using("System.Text")
                .Using("Obsidian.API.Utilities")
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

            SetStateFromIdMethod(fullName, stateBuilder, block.Properties);

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

        stateBuilder.Statement("var list = new List<string>()");

        foreach (var property in properties)
        {
            var name = property.Name;
            var sanitizedName = name.Replace("Is", string.Empty);

            stateBuilder.Line($"this.valueStore[\"{name}\"].GetIndexFromArray(this.{sanitizedName}.ToString()),");
        }

        stateBuilder.EndScope(true).Line();

        stateBuilder.Line().Line($"var stateId = this.possibleStates[string.Join(\"-\", list)];");
    }
}
