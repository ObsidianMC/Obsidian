using System.Reflection;
using System.Text.Json;

namespace Obsidian.WorldData.Portals;

/// <summary>Vanilla's nearest free portal exit, using the collision boxes dumped from the server jar.</summary>
internal static class PortalCollision
{
    private static readonly Lazy<Dictionary<int, BoundingBox[]>> shapes = new(LoadShapes);

    internal static IReadOnlyList<BoundingBox> GetShapes(IBlock block) => block.HasEmptyCollision()
        ? [] : shapes.Value.GetValueOrDefault(block.GetHashCode()) ?? [];

    public static VectorD FindFreePosition(Func<Vector, IBlock> read, VectorD position, double width, double height)
    {
        if (width > 4 || height > 4)
            return position;
        var halfWidth = width / 2;
        var radius = halfWidth + 1;
        var obstacles = new List<BoundingBox>();
        for (var x = (int)Math.Floor(position.X - radius - halfWidth); x <= Math.Floor(position.X + radius + halfWidth); x++)
            for (var y = (int)Math.Floor(position.Y - 1); y <= Math.Floor(position.Y + height + 1); y++)
                for (var z = (int)Math.Floor(position.Z - radius - halfWidth); z <= Math.Floor(position.Z + radius + halfWidth); z++)
                {
                    var block = read(new Vector(x, y, z));
                    if (block.HasEmptyCollision())
                        continue;
                    if (!shapes.Value.TryGetValue(block.GetHashCode(), out var boxes))
                        continue;
                    foreach (var box in boxes)
                    {
                        // Forbidden feet positions: inflate the block's shape by the player's dimensions.
                        obstacles.Add(new BoundingBox(new VectorD(x + box.Min.X - halfWidth, y + box.Min.Y - height, z + box.Min.Z - halfWidth),
                            new VectorD(x + box.Max.X + halfWidth, y + box.Max.Y, z + box.Max.Z + halfWidth)));
                    }
                }
        bool IsFree(VectorD point) => obstacles.All(box => point.X <= box.Min.X || point.X >= box.Max.X ||
            point.Y <= box.Min.Y || point.Y >= box.Max.Y || point.Z <= box.Min.Z || point.Z >= box.Max.Z);
        if (IsFree(position))
            return position;

        var xs = Candidates(position.X, radius, obstacles.SelectMany(box => new[] { box.Min.X, box.Max.X }));
        var ys = Candidates(position.Y, 1, obstacles.SelectMany(box => new[] { box.Min.Y, box.Max.Y }));
        var zs = Candidates(position.Z, radius, obstacles.SelectMany(box => new[] { box.Min.Z, box.Max.Z }));
        var best = position;
        var bestDistance = double.MaxValue;
        foreach (var x in xs)
            foreach (var y in ys)
                foreach (var z in zs)
                {
                    var distance = Math.Pow(x - position.X, 2) + Math.Pow(y - position.Y, 2) + Math.Pow(z - position.Z, 2);
                    var candidate = new VectorD(x, y, z);
                    if (distance < bestDistance && LevelPortals.InsideBorder((Vector)candidate) && IsFree(candidate))
                    {
                        best = candidate;
                        bestDistance = distance;
                    }
                }
        return best;
    }

    private static double[] Candidates(double center, double radius, IEnumerable<double> edges) =>
        edges.Append(center).Append(center - radius).Append(center + radius)
            .Where(value => value >= center - radius && value <= center + radius).Distinct()
            .OrderBy(value => Math.Abs(value - center)).ToArray();

    private static Dictionary<int, BoundingBox[]> LoadShapes()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Obsidian.Assets.block_physics.json")
            ?? throw new InvalidOperationException("Missing block physics asset.");
        using var document = JsonDocument.Parse(stream);
        var boxes = document.RootElement.GetProperty("collisionShapes").EnumerateArray()
            .Select(shape => shape.EnumerateArray().Select(box => new BoundingBox(
                new VectorD(box[0].GetDouble(), box[1].GetDouble(), box[2].GetDouble()),
                new VectorD(box[3].GetDouble(), box[4].GetDouble(), box[5].GetDouble()))).ToArray()).ToArray();
        return document.RootElement.GetProperty("collisionShapeIds").EnumerateArray()
            .Select((shape, state) => (State: state, Boxes: boxes[shape.GetInt32()]))
            .ToDictionary(entry => entry.State, entry => entry.Boxes);
    }
}
