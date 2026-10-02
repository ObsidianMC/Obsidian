using System.Reflection;
using System.Text.Json;

namespace Obsidian.Registries;

/// <summary>
/// Vanilla's per-state light properties, used by the light engine. Light emission is in <see cref="BlockPhysics"/>.
/// </summary>
/// <remarks>
/// Loaded from <c>Assets/block_light.json</c>, which was dumped from vanilla 1.21.11 for every block state. <c>light</c>
/// packs, per state, the light block (bits 0-3) and a 1-based index into <c>shapes</c> (bits 4 and up, 0 for states that
/// don't use their shape for light occlusion). Each shape lists one <c>faces</c> mask per <see cref="BlockFace"/>: a 16x16
/// bit grid of the face's occlusion shape, as four 64-bit words in hex. Two faces block light exactly when their masks
/// together cover the grid, which reproduces vanilla's <c>Shapes.faceShapeOccludes</c> for every pair of faces (checked
/// when the table was dumped).
/// </remarks>
internal static class BlockLight
{
    private static readonly Lazy<(int[] Light, ulong[] Faces)> data = new(Load);

    /// <summary>
    /// Vanilla <c>BlockState.getLightBlock()</c>: how much light the block absorbs. 0 for blocks that sky light passes
    /// straight down through, 15 for opaque full blocks and 1 for everything in between (leaves, water, ice...).
    /// </summary>
    public static int LightBlock(this IBlock block) => Light(block) & 15;

    /// <summary>
    /// Vanilla <c>LightEngine.shapeOccludes</c>: whether the touching faces of <paramref name="from"/> and its neighbor
    /// <paramref name="to"/> on its <paramref name="direction"/> side together close the gap, so no light crosses it.
    /// Only blocks that use their shape for light occlusion (slabs, stairs, snow layers and the like) have such faces.
    /// </summary>
    public static bool LightShapesOcclude(IBlock from, IBlock to, BlockFace direction)
    {
        var fromShape = Light(from) >> 4;
        var toShape = Light(to) >> 4;
        if (fromShape == 0 && toShape == 0)
            return false;

        // Opposite faces differ only in their lowest bit (down/up, north/south, west/east).
        var faces = data.Value.Faces;
        var fromFace = (fromShape * 6 + (int)direction) * 4;
        var toFace = (toShape * 6 + ((int)direction ^ 1)) * 4;
        for (var word = 0; word < 4; word++)
        {
            if ((faces[fromFace + word] | faces[toFace + word]) != ulong.MaxValue)
                return false;
        }

        return true;
    }

    private static int Light(IBlock block)
    {
        var light = data.Value.Light;
        var id = block.GetHashCode();
        return (uint)id < (uint)light.Length ? light[id] : 0;
    }

    private static (int[] Light, ulong[] Faces) Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Obsidian.Assets.block_light.json")
            ?? throw new InvalidOperationException("Missing block light asset.");
        using var document = JsonDocument.Parse(stream);

        var root = document.RootElement;
        var light = root.GetProperty("light").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        var masks = root.GetProperty("faces").EnumerateArray()
            .Select(value => value.GetString()!)
            .Select(hex => Enumerable.Range(0, 4).Select(word => Convert.ToUInt64(hex.Substring(word * 16, 16), 16)).ToArray())
            .ToArray();

        // Face masks are flattened per shape and face, 4 words each. Shape 0 (no shape occlusion) has empty faces.
        var shapes = root.GetProperty("shapes").EnumerateArray().ToArray();
        var faces = new ulong[(shapes.Length + 1) * 6 * 4];
        for (var shape = 0; shape < shapes.Length; shape++)
        {
            var face = 0;
            foreach (var mask in shapes[shape].EnumerateArray())
            {
                masks[mask.GetInt32()].CopyTo(faces, ((shape + 1) * 6 + face) * 4);
                face++;
            }
        }

        return (light, faces);
    }
}
