using Obsidian.SourceGenerators.Registry.Models;

namespace Obsidian.SourceGenerators.Registry;

public partial class RegistryAssetsGenerator
{
    // Vanilla's block ids as constants, for switches and patterns over blocks in the server's vanilla code.
    private static void GenerateBlockIdConstants(Assets assets, SourceProductionContext context)
    {
        var builder = new CodeBuilder();
        builder.Namespace("Obsidian.API.Registries");
        builder.Line();
        builder.Line("/// <summary>");
        builder.Line("/// Vanilla's block ids (<see cref=\"Obsidian.API.IBlock.RegistryId\"/>), as constants");
        builder.Line("/// to match blocks by in switches and patterns. Plugins' blocks have no constant: compare");
        builder.Line("/// them by their block type.");
        builder.Line("/// </summary>");
        builder.Type("public static class BlockIds");

        foreach (Block block in assets.Blocks.OrderBy(block => block.RegistryId))
            builder.Line($"public const int {block.Name} = {block.RegistryId};");

        builder.EndScope();

        context.AddSource("BlockIds.g.cs", builder.ToString());
    }

    private static void GenerateBlockIds(Assets assets, SourceProductionContext context)
    {
        var builder = new CodeBuilder();

        builder.Namespace("Obsidian.Registries");
        builder.Line();
        builder.Type("internal static partial class BlocksRegistry");

        var blocks = assets.Blocks.OrderBy(block => block.RegistryId);

        builder.Indent().Append("internal static readonly ushort[] AllStates = { ");
        foreach (Block block in blocks)
        {
            foreach(var keys in block.StateValues.Keys)
            {
                string entry = $"{keys}, ";
                builder.Append(entry);
            }
        }
        builder.Append("};").Line();

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
            string entry = $"{block.RegistryId}, ";
            for (int i = 0; i < block.StatesCount; i++)
                builder.Append(entry);
        }
        builder.Append("};").Line();

        builder.Indent().Append("internal static readonly string[] ResourceIds = { ");
        foreach (Block block in blocks)
        {
            builder.Append($"\"{block.Tag}\", ");
        }
        builder.Append("};").Line();

        builder.Indent().Append("internal static readonly string[] Names = { ");
        foreach (Block block in blocks)
        {
            builder.Append($"\"{block.Name}\", ");
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
