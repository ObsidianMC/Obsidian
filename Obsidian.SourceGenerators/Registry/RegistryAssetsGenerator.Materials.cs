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

        var addedButton = false;

        foreach (string material in materials)
        {
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
