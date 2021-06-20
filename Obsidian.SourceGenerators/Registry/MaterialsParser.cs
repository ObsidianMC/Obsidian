using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Obsidian.SourceGenerators.Registry
{
    // Purposefully not an AssetsParser. Is executed first,
    // because other parsers depends on it. Combines multiple
    // asset files.
    internal sealed class MaterialsParser
    {
        public static IReadOnlyList<(string namespacedName, string name)> Materials => _materials;
        public static IReadOnlyDictionary<string, string> Names => _names;

        private static List<(string namespacedName, string name)> _materials = new();
        private static Dictionary<string, string> _names = new();

        public MaterialsParser()
        {
        }

        public void ParseMaterials(GeneratorExecutionContext context)
        {
            string assemblyName = context.Compilation.AssemblyName;
            if (assemblyName != "Obsidian" && assemblyName != "Obsidian.API")
                return;

            ParseAsset("blocks.json", context);
            ParseAsset("items.json", context);

            _materials = _materials.Distinct().ToList();
            foreach (var (namespacedName, name) in _materials)
            {
                _names[namespacedName] = name;
            }

            if (assemblyName == "Obsidian.API")
                BuildEnum(context);
        }

        private void ParseAsset(string assetName, GeneratorExecutionContext context)
        {
            string assetJson = context.GetAsset(assetName);
            if (assetJson is null)
            {
                context.ReportDiagnostic(DiagnosticSeverity.Error, $"Missing '{assetName}' asset in {context.Compilation.AssemblyName}.");
                throw new FileNotFoundException();
            }

            using var json = JsonDocument.Parse(assetJson);
            foreach (JsonProperty property in json.RootElement.EnumerateObject())
            {
                _materials.Add((property.Name, property.Name.RemoveNamespace().ToPascalCase()));
            }
        }

        private void BuildEnum(GeneratorExecutionContext context)
        {
            var builder = new CodeBuilder();

            builder.Namespace(context.Compilation.AssemblyName);
            builder.Type("public enum Material");

            foreach (var material in _materials)
            {
                builder.Line($"{material.name},");
            }

            builder.EndScope();
            builder.EndScope();

            context.AddSource("Material.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
        }
    }
}
