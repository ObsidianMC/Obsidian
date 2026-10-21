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
        var treeGrowers = new CodeBuilder();
        var placementBehaviors = compilation.GetTypeByMetadataName("Obsidian.WorldData.PlacementBehaviors");
        // Each virtual hook is bound from the vanilla class that declares its override to a same-named member of the
        // hook's counterpart type.
        var hooks = OwnedHooks
            .Select(hook => (hook, type: compilation.GetTypeByMetadataName($"Obsidian.WorldData.{hook.Counterparts}"), body: new CodeBuilder()))
            .ToArray();

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

            // A sapling's TreeGrower, as vanilla's SaplingBlock holds it; the sapling's random tick owner is a separate
            // association (the hooks below).
            if (block.TreeGrower is { } grower)
            {
                var initializer = string.Join(", ", grower.Select(entry => entry.Key == "secondaryChance"
                    ? $"SecondaryChance = {entry.Value}"
                    : $"{entry.Key.ToPascalCase()} = \"{entry.Value}\""));
                treeGrowers.Indent().Append($"[{block.RegistryId}] = new Obsidian.API.Blocks.TreeGrower {{ {initializer} }},").Line();
            }

            string? placementMember = OwnerMember(block.PlacementClass);
            if (placementMember is not null && placementBehaviors?.GetMembers(placementMember).Length > 0)
                placementBehavior.Indent().Append($"{block.RegistryId} => PlacementBehaviors.{placementMember},").Line();

            // An owner without a counterpart keeps the existing path (Growth, BlockBehaviors); the info diagnostic
            // lists the owners still to migrate, without a warning.
            foreach (var (hook, type, body) in hooks)
            {
                if (type is null || !block.OverrideClasses.TryGetValue(hook.Name, out var ownerClass) || OwnerMember(ownerClass) is not string member)
                    continue;

                if (type.GetMembers(member).Length > 0)
                    body.Indent().Append($"{block.RegistryId} => {hook.Counterparts}.{member},").Line();
                else
                    ctx.ReportDiagnostic(Diagnostic.Create(UnmatchedOwner, Location.None, hook.Name, ownerClass, hook.Counterparts));
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
            .Line("/// <summary>The tree a sapling block grows into, from vanilla's SaplingBlock.treeGrower, or null for other blocks.</summary>")
            .Line("internal static Obsidian.API.Blocks.TreeGrower? GetTreeGrower(int registryId) =>")
            .Line("    treeGrowers.TryGetValue(registryId, out var grower) ? grower : null;")
            .Line()
            .Line("private static readonly System.Collections.Generic.Dictionary<int, Obsidian.API.Blocks.TreeGrower> treeGrowers = new()")
            .Line("{")
            .Append(treeGrowers.ToString())
            .Line("};")
            .Line()
            .Line("/// <summary>The behaviour supplied by the vanilla class that owns this block's placement override.</summary>")
            .Line("internal static IBlockBehavior? GetPlacementBehavior(int registryId) => registryId switch")
            .Line("{")
            .Append(placementBehavior.ToString())
            .Line("    _ => null,")
            .Line("};");

        foreach (var (hook, type, body) in hooks)
        {
            if (type is null)
                continue;

            blocksBuilder.Line()
                .Line($"/// <summary>The behaviour supplied by the vanilla class that owns this block's {hook.Name} override, or null.</summary>")
                .Line($"internal static {hook.Interface}? {hook.Method}(int registryId) => registryId switch")
                .Line("{")
                .Append(body.ToString())
                .Line("    _ => null,")
                .Line("};");
        }

        blocksBuilder.EndScope();

        ctx.AddSource("BlocksRegistry.Blocks.g.cs", blocksBuilder.ToString());
    }

    private sealed class OwnedHook(string name, string counterparts, string @interface, string method)
    {
        public string Name { get; } = name;
        public string Counterparts { get; } = counterparts;
        public string Interface { get; } = @interface;
        public string Method { get; } = method;
    }

    private static readonly OwnedHook[] OwnedHooks =
    [
        new("randomTick", "RandomTickBehaviors", "IRandomTickBehavior", "GetRandomTickBehavior"),
        new("tick", "TickBehaviors", "IScheduledTickBehavior", "GetTickBehavior"),
        new("updateShape", "ShapeUpdateBehaviors", "IShapeUpdateBehavior", "GetShapeUpdateBehavior")
    ];

    private static readonly DiagnosticDescriptor UnmatchedOwner = new("OBSGEN001",
        "Vanilla behavior owner has no Obsidian counterpart",
        "Vanilla {0} owner {1} has no member in Obsidian.WorldData.{2}; its blocks use the existing path",
        "Blocks", DiagnosticSeverity.Info, isEnabledByDefault: true);

    private static string? OwnerMember(string? ownerClass)
    {
        if (ownerClass is null)
            return null;

        int separator = Math.Max(ownerClass.LastIndexOf('.'), ownerClass.LastIndexOf('$'));
        return ownerClass.Substring(separator + 1).SanitizeBlockName();
    }

}
