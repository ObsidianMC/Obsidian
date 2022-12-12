using Obsidian.SourceGenerators.Registry.Models;

namespace Obsidian.SourceGenerators.Registry;

public partial class RegistryAssetsGenerator
{
   
   
    private static void GenerateMaterials(Assets assets, GeneratorExecutionContext context)
    {
        IEnumerable<string> materials =
            assets.Blocks.OrderBy(block => block.BaseId).Select(block => block.Name)
            .Concat(assets.Items.Select(item => item.Name))
            .Distinct();

        var builder = new CodeBuilder();
        builder.Namespace("Obsidian.API");
        builder.Line();

        builder.Type("public enum Material");

        var added = new HashSet<string>();
        var addedButton = false;

        foreach (string material in materials)
        {
            var match = BlockGenerator.colorRegex.Match(material);

            if (match.Success && !BlockGenerator.ignored.Contains(material))
            {
                var color = match.Value;

                var na = material.Replace(color, string.Empty);

                if (BlockGenerator.filters.Contains(na))
                    na = $"Colored{na}";

                if (!added.Add(na))
                    continue;

                builder.Line($"{na},");

                continue;
            }

            if (material.EndsWith("Button") && !addedButton)
            {
                builder.Line("Button,");
                addedButton = true;
                continue;
            }

            builder.Line($"{material},");
        }

        builder.EndScope();

        context.AddSource("Material.g.cs", builder.ToString());
    }
}
