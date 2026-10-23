using Obsidian.WorldData.Structures;
using System.Reflection;
using System.Text.Json;

namespace Obsidian.Registries;

/// <summary>
/// Vanilla's <c>BlockState.rotate</c> and <c>BlockState.mirror</c> for every block state, plugins' blocks included.
/// </summary>
/// <remarks>
/// Vanilla states use <c>Assets/block_transforms.json</c>, dumped from vanilla 1.21.11: per transform, the resulting
/// state id minus the state id for each state. Plugin blocks aren't in it; they're turned by their properties instead
/// (see <see cref="TransformProperties"/>), which needs no table since their properties never change while the server
/// runs.
/// </remarks>
internal static class BlockTransforms
{
    private static readonly Lazy<int[][]> data = new(Load);

    public static IBlock Rotate(this IBlock block, StructureRotation rotation) => rotation switch
    {
        StructureRotation.Clockwise90 => Transform(block, 0, rotation, StructureMirror.None),
        StructureRotation.Clockwise180 => Transform(block, 1, rotation, StructureMirror.None),
        StructureRotation.CounterClockwise90 => Transform(block, 2, rotation, StructureMirror.None),
        _ => block
    };

    public static IBlock Mirror(this IBlock block, StructureMirror mirror) => mirror switch
    {
        StructureMirror.LeftRight => Transform(block, 3, StructureRotation.None, mirror),
        StructureMirror.FrontBack => Transform(block, 4, StructureRotation.None, mirror),
        _ => block
    };

    /// <summary>
    /// The property values that change when a state with <paramref name="values"/> is turned by
    /// <paramref name="rotation"/> and then mirrored by <paramref name="mirror"/>, by the rules vanilla's blocks follow
    /// for properties of these names: a <c>facing</c> direction turns, an <c>axis</c> swaps <c>x</c> and <c>z</c> on a
    /// quarter turn, a <c>rotation</c> of 16 steps turns like a standing sign's, and <c>north</c>, <c>east</c>,
    /// <c>south</c> and <c>west</c> connections trade values like a fence's. Other properties are kept.
    /// </summary>
    internal static Dictionary<string, string> TransformProperties(
        IReadOnlyDictionary<string, string> values,
        StructureRotation rotation,
        StructureMirror mirror)
    {
        var changed = new Dictionary<string, string>();

        foreach (var (name, value) in values)
        {
            switch (name)
            {
                case "facing" when ParseFace(value) is BlockFace facing:
                    changed[name] = FaceName(mirror.Mirror(rotation.Rotate(facing)));
                    break;

                case "axis" when (int)rotation % 2 == 1 && value is "x" or "z":
                    changed[name] = value == "x" ? "z" : "x";
                    break;

                case "rotation" when int.TryParse(value, out var segment) && segment is >= 0 and < 16:
                    changed[name] = TurnSegment(segment, rotation, mirror).ToString();
                    break;

                default:
                    if (ParseFace(name) is BlockFace side and not (BlockFace.Up or BlockFace.Down))
                        changed[FaceName(mirror.Mirror(rotation.Rotate(side)))] = value;

                    break;
            }
        }

        return changed;
    }

    private static IBlock Transform(IBlock block, int transform, StructureRotation rotation, StructureMirror mirror)
    {
        var deltas = data.Value[transform];
        var id = block.GetHashCode();

        if ((uint)id < (uint)deltas.Length)
            return deltas[id] != 0 ? BlocksRegistry.Get(id + deltas[id]) : block;

        if (!BlocksRegistry.TryGetPluginBlock(id, out var type))
            return block;

        // A state the block doesn't have (a facing it can't take, say) leaves the block as it is.
        var turned = type.TryWith(id, TransformProperties(type.ValuesOf(id), rotation, mirror));
        return turned < 0 ? block : type.StateOf(turned);
    }

    // Vanilla's Rotation.rotate and Mirror.mirror for a value out of 16 (a standing sign's rotation, 0 facing south and
    // counting clockwise): a quarter turn is 4 steps, mirroring flips it across the mirrored axis. Signs, banners and
    // skulls in structures depend on it.
    private static int TurnSegment(int segment, StructureRotation rotation, StructureMirror mirror)
    {
        segment = (segment + 4 * (int)rotation) & 15;

        return mirror switch
        {
            StructureMirror.LeftRight => (8 - segment) & 15,
            StructureMirror.FrontBack => -segment & 15,
            _ => segment
        };
    }

    private static BlockFace? ParseFace(string name) => name switch
    {
        "down" => BlockFace.Down,
        "up" => BlockFace.Up,
        "north" => BlockFace.North,
        "south" => BlockFace.South,
        "west" => BlockFace.West,
        "east" => BlockFace.East,
        _ => null
    };

    private static string FaceName(BlockFace face) => face switch
    {
        BlockFace.Down => "down",
        BlockFace.Up => "up",
        BlockFace.North => "north",
        BlockFace.South => "south",
        BlockFace.West => "west",
        _ => "east"
    };

    private static int[][] Load()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Obsidian.Assets.block_transforms.json")
            ?? throw new InvalidOperationException("Missing block transforms asset.");
        using var document = JsonDocument.Parse(stream);

        var root = document.RootElement;
        string[] transforms =
        [
            "rotateClockwise90",
            "rotate180",
            "rotateCounterclockwise90",
            "mirrorLeftRight",
            "mirrorFrontBack"
        ];

        return
        [
            .. transforms.Select(name => root.GetProperty(name)
                .EnumerateArray()
                .Select(value => value.GetInt32())
                .ToArray())
        ];
    }
}
