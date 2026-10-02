using Obsidian.WorldData.Structures;
using System.Reflection;
using System.Text.Json;

namespace Obsidian.Registries;

/// <summary>
/// Vanilla's <c>BlockState.rotate</c> and <c>BlockState.mirror</c> for every block state.
/// </summary>
/// <remarks>
/// Loaded from <c>Assets/block_transforms.json</c>, dumped from vanilla 1.21.11: per transform, the resulting state id
/// minus the state id for each state.
/// </remarks>
internal static class BlockTransforms
{
    private static readonly Lazy<int[][]> data = new(Load);

    public static IBlock Rotate(this IBlock block, StructureRotation rotation) => rotation switch
    {
        StructureRotation.Clockwise90 => Transform(block, 0),
        StructureRotation.Clockwise180 => Transform(block, 1),
        StructureRotation.CounterClockwise90 => Transform(block, 2),
        _ => block
    };

    public static IBlock Mirror(this IBlock block, StructureMirror mirror) => mirror switch
    {
        StructureMirror.LeftRight => Transform(block, 3),
        StructureMirror.FrontBack => Transform(block, 4),
        _ => block
    };

    private static IBlock Transform(IBlock block, int transform)
    {
        var deltas = data.Value[transform];
        var id = block.GetHashCode();
        return (uint)id < (uint)deltas.Length && deltas[id] != 0 ? BlocksRegistry.Get(id + deltas[id]) : block;
    }

    private static int[][] Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Obsidian.Assets.block_transforms.json")
            ?? throw new InvalidOperationException("Missing block transforms asset.");
        using var document = JsonDocument.Parse(stream);

        var root = document.RootElement;
        return [.. new[] { "rotateClockwise90", "rotate180", "rotateCounterclockwise90", "mirrorLeftRight", "mirrorFrontBack" }
            .Select(name => root.GetProperty(name).EnumerateArray().Select(value => value.GetInt32()).ToArray())];
    }
}
