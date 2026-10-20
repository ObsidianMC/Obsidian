using Obsidian.SourceGenerators.Registry.Models;
using System.Diagnostics;

namespace Obsidian.SourceGenerators.Registry;

public partial class BlocksGenerator
{
    private static void GenerateBlocks(Block[] blocks, Compilation compilation, SourceProductionContext ctx)
    {
        var blocksBuilder = new CodeBuilder()
            .Line("#nullable enable")
            .Using("Obsidian.API.Blocks")
            .Using("Obsidian.Blocks")
            .Using("Obsidian.WorldData")
            .Line()
            .Namespace("Obsidian.Registries")
            .Type("internal partial class BlocksRegistry");

        // Factories by registry id, so the registry builds its blocks without reflection.
        var createDefault = new CodeBuilder();
        var createState = new CodeBuilder();
        var placementBehavior = new CodeBuilder();
        var placementBehaviors = compilation.GetTypeByMetadataName("Obsidian.WorldData.PlacementBehaviors");

        foreach (var block in blocks)
        {
            var blockName = block.Name.SanitizeBlockName();

            var builder = new CodeBuilder()
                .Using("Obsidian.API")
                .Using("Obsidian.API.BlockStates")
                .Using("Obsidian.API.BlockStates.Builders")
                .Line()
                .Namespace("Obsidian.Blocks")
                .Line()
                .Type($"public readonly struct {blockName} : IBlock");

            builder.Line($"public string UnlocalizedName => \"{block.Tag}\";");
            builder.Line($"public int BaseId => {block.BaseId};");
            builder.Line($"public int DefaultId => {block.DefaultId};");
            builder.Line($"public int RegistryId => {block.RegistryId};");

            builder.Line("public IBlockState State { get; init; }");

            builder.Line().Method($"public {blockName}()").EndScope();

            if (block.Properties.Length > 0)
            {
                builder.Line().Method($"public {blockName}(int stateId)");

                builder.Line($"this.State = new {blockName}StateBuilder(stateId).Build();");
                builder.EndScope();

                builder.Line().Method($"public {blockName}(IBlockState state)");
                builder.Line("ArgumentNullException.ThrowIfNull(state, \"state\");");
                builder.Line("this.State = state;").EndScope();
            }

            builder.Line()
                .Method("public override int GetHashCode()")
                .Line($"return this.State != null ? this.State.Id : this.DefaultId;")
                .EndScope();

            builder.EndScope();
            ctx.AddSource($"{blockName}.g.cs", builder.ToString());

            blocksBuilder.Indent().Append($"public static readonly IBlock {blockName} = new {blockName}();").Line();

            createDefault.Indent().Append($"{block.RegistryId} => new {blockName}(),").Line();
            if (block.Properties.Length > 0)
                createState.Indent().Append($"{block.RegistryId} => new {blockName}(stateId),").Line();

            string? placementMember = PlacementMember(block.PlacementClass);
            if (placementMember is not null && placementBehaviors?.GetMembers(placementMember).Length > 0)
                placementBehavior.Indent().Append($"{block.RegistryId} => PlacementBehaviors.{placementMember},").Line();
        }

        blocksBuilder.Line()
            .Line("/// <summary>The block of a registry id without a state, which reads as its default state.</summary>")
            .Line("private static IBlock CreateDefault(int registryId) => registryId switch")
            .Line("{")
            .Append(createDefault.ToString())
            .Line("    _ => throw new ArgumentOutOfRangeException(nameof(registryId)),")
            .Line("};")
            .Line()
            .Line("/// <summary>The block of a registry id in a state, or <c>null</c> for blocks without properties.</summary>")
            .Line("private static IBlock? CreateState(int registryId, int stateId) => registryId switch")
            .Line("{")
            .Append(createState.ToString())
            .Line("    _ => null,")
            .Line("};")
            .Line()
            .Line("/// <summary>The behaviour supplied by the vanilla class that owns this block's placement override.</summary>")
            .Line("internal static IBlockBehavior? GetPlacementBehavior(int registryId) => registryId switch")
            .Line("{")
            .Append(placementBehavior.ToString())
            .Line("    _ => null,")
            .Line("};");

        blocksBuilder.EndScope();

        ctx.AddSource("BlocksRegistry.Blocks.g.cs", blocksBuilder.ToString());
    }

    private static string? PlacementMember(string? placementClass)
    {
        if (placementClass is null)
            return null;

        int separator = Math.Max(placementClass.LastIndexOf('.'), placementClass.LastIndexOf('$'));
        return placementClass.Substring(separator + 1).SanitizeBlockName();
    }

}
