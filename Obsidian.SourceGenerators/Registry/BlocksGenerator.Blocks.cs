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
        var randomTickBehaviors = compilation.GetTypeByMetadataName("Obsidian.WorldData.RandomTickBehaviors");
        var randomTickBehavior = new CodeBuilder();

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

            string? placementMember = OwnerMember(block.PlacementClass);
            if (placementMember is not null && placementBehaviors?.GetMembers(placementMember).Length > 0)
                placementBehavior.Indent().Append($"{block.RegistryId} => PlacementBehaviors.{placementMember},").Line();

            // A random tick owner without a counterpart keeps the existing path (Growth, BlockBehaviors); the info
            // diagnostic lists the owners still to migrate, without a warning.
            if (randomTickBehaviors is not null && OwnerMember(block.RandomTickClass) is string randomTickMember)
            {
                if (randomTickBehaviors.GetMembers(randomTickMember).Length > 0)
                    randomTickBehavior.Indent().Append($"{block.RegistryId} => RandomTickBehaviors.{randomTickMember},").Line();
                else
                    ctx.ReportDiagnostic(Diagnostic.Create(UnmatchedRandomTickOwner, Location.None, block.RandomTickClass));
            }
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

        if (randomTickBehaviors is not null)
        {
            blocksBuilder.Line()
                .Line("/// <summary>The behaviour supplied by the vanilla class that owns this block's randomTick override, or null.</summary>")
                .Line("internal static IRandomTickBehavior? GetRandomTickBehavior(int registryId) => registryId switch")
                .Line("{")
                .Append(randomTickBehavior.ToString())
                .Line("    _ => null,")
                .Line("};");
        }

        blocksBuilder.EndScope();

        ctx.AddSource("BlocksRegistry.Blocks.g.cs", blocksBuilder.ToString());
    }

    private static readonly DiagnosticDescriptor UnmatchedRandomTickOwner = new("OBSGEN001",
        "Vanilla random tick owner has no Obsidian counterpart",
        "Vanilla random tick owner {0} has no member in Obsidian.WorldData.RandomTickBehaviors; its blocks use the existing growth path",
        "Blocks", DiagnosticSeverity.Info, isEnabledByDefault: true);

    private static string? OwnerMember(string? ownerClass)
    {
        if (ownerClass is null)
            return null;

        int separator = Math.Max(ownerClass.LastIndexOf('.'), ownerClass.LastIndexOf('$'));
        return ownerClass.Substring(separator + 1).SanitizeBlockName();
    }

}
