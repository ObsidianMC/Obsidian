using Obsidian.SourceGenerators.Registry.Models;

namespace Obsidian.SourceGenerators.Registry;

public partial class RegistryAssetsGenerator
{
    private static void GenerateBlockIds(Assets assets, GeneratorExecutionContext context)
    {
        var builder = new CodeBuilder();

        builder.Namespace("Obsidian.Utilities.Registry");
        builder.Line();
        builder.Type("internal static class BlocksRegistry");

        var blocks = assets.Blocks.OrderBy(block => block.BaseId);

        builder.Indent().Append("internal static readonly ushort[] StateToBase = { ");
        foreach (Block block in blocks)
        {
            string entry = $"{block.BaseId}, ";
            for (int i = 0; i < block.StatesCount; i++)
                builder.Append(entry);
        }
        builder.Append("};").Line();

        builder.Indent().Append("internal static readonly ushort[] StateToNumeric = { ");
        foreach (Block block in blocks)
        {
            string entry = $"{block.NumericId}, ";
            for (int i = 0; i < block.StatesCount; i++)
                builder.Append(entry);
        }
        builder.Append("};").Line();

        builder.Indent().Append("internal static readonly string[] Names = { ");
        foreach (Block block in blocks)
        {
            builder.Append($"\"{block.Tag}\", ");
        }
        builder.Append("};").Line();

        builder.Indent().Append("internal static readonly ushort[] NumericToBase = { ");
        foreach (Block block in blocks)
        {
            builder.Append($"{block.BaseId}, ");
        }
        builder.Append("};").Line();

        builder.EndScope();

        context.AddSource("BlocksRegistry.g.cs", builder.ToString());
    }
}
