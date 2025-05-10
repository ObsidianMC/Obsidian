using Obsidian.SourceGenerators.Registry.Models;
using static Obsidian.SourceGenerators.Constants;

namespace Obsidian.SourceGenerators.Registry;
public partial class WorldgenNoiseRegistryGenerator
{
    private static void BuildDensityFunctions(CleanedNoises cleanedNoises, Noises noises, CodeBuilder builder)
    {
        var densityFunctions = noises.DensityFunctions;

        builder.Type("public static class DensityFunctions");

        var groups = new Dictionary<string, Dictionary<string, List<BaseFeature>>>();

        foreach (var function in densityFunctions)
        {
            var typeName = function.Name.Replace(DensityFunction, string.Empty);

            var split = typeName.Split('\\');

            var group = split.Length > 1 ? split[0] : Default;
            var secondGroup = split.Length > 2 ? split[1] : Default;

            if (groups.TryGetValue(group, out var dict))
            {
                if(dict.TryGetValue(secondGroup, out var list))
                    list.Add(function);
                else
                    dict[secondGroup] = [function];
            }
            else
                groups[group] = new() { { secondGroup, new() { function } } };
            
        }

        foreach (var group in groups.OrderBy(x => x.Key))
        {
            var hasGroup = group.Key != Default;

            if (hasGroup)
                builder.Type($"public static class {group.Key.ToPascalCase()}");

            foreach (var secondGroup in group.Value)
            {
                var hasSecondGroup = secondGroup.Key != Default;

                if (hasSecondGroup)
                    builder.Type($"public static class {secondGroup.Key.ToPascalCase()}");

                foreach (var function in secondGroup.Value)
                {
                    if (function.Properties.Count == 0)
                    {
                        builder.Type("public static IDensityFunction Zero => new ConstantDensityFunction()");

                        builder.Line("Argument = 0.0d");

                        builder.EndScope(true);

                        continue;
                    }

                    var typeName = function.Name.Replace(DensityFunction, string.Empty);
                    var split = typeName.Split('\\');

                    var skipAmount = split.Length > 1 ? split.Length - 1 : 0;
                    var sanitizedName = string.Join(string.Empty, split.Skip(skipAmount)).ToPascalCase();

                    var functionTypeName = function.Properties.First(x => x.Name == "type").Value.GetString()!;
                    if (!cleanedNoises.WorldgenProperties.TryGetValue(functionTypeName, out var typeInformation))
                        continue;

                    builder.Type($"public static IDensityFunction {sanitizedName} => new {typeInformation.Symbol.Name}()");

                    foreach (var property in function.Properties)
                    {
                        var elementName = property.Name;
                        var element = property.Value;

                        AppendChildProperty(cleanedNoises, elementName, element, builder, true, true, typeInformation);
                    }

                    builder.EndScope(true);
                }

                if (hasSecondGroup)
                    builder.EndScope();
            }

            if (hasGroup)
                builder.EndScope();
        }

        builder.Type("public static readonly FrozenDictionary<string, IDensityFunction> All = new Dictionary<string, IDensityFunction>()");

        foreach (var function in densityFunctions)
        {
            var cleanedName = function.Name.Replace(DensityFunction, string.Empty).Replace("\\", "/");

            var split = cleanedName.Split('/');

            var list = new List<string>();

            foreach (var element in split)
                list.Add(element.ToPascalCase());

            var callableName = string.Join(".", list);

            builder.Line($"{{ \"minecraft:{cleanedName}\", {callableName}}}, ");
        }

        builder.EndScope(".ToFrozenDictionary()", true);

        builder.EndScope();
    }
}
