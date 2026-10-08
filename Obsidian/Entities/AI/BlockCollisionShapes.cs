using System.IO;
using System.Reflection;
using System.Text.Json;

namespace Obsidian.Entities.AI;

internal static class BlockCollisionShapes
{
    private static readonly Dictionary<int, BoundingBox[]> shapes = Load();

    public static IReadOnlyList<BoundingBox> Get(IBlock block) => Get(block.State?.Id ?? block.DefaultId);

    public static IReadOnlyList<BoundingBox> Get(int stateId) => shapes.TryGetValue(stateId, out var shape)
        ? shape
        : throw new InvalidDataException($"No collision shape for block state {stateId}.");

    private static Dictionary<int, BoundingBox[]> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Obsidian.Assets.collision_shapes.json")
            ?? throw new InvalidDataException("Missing block collision shapes.");
        using var document = JsonDocument.Parse(stream);
        var shapeDefinitions = new Dictionary<int, BoundingBox[]>();
        foreach (var entry in document.RootElement.GetProperty("shapes").EnumerateObject())
        {
            shapeDefinitions.Add(int.Parse(entry.Name), entry.Value.EnumerateArray().Select(box =>
                new BoundingBox(new VectorD(box[0].GetDouble(), box[1].GetDouble(), box[2].GetDouble()),
                    new VectorD(box[3].GetDouble(), box[4].GetDouble(), box[5].GetDouble()))).ToArray());
        }

        return document.RootElement.GetProperty("states").EnumerateObject()
            .ToDictionary(entry => int.Parse(entry.Name), entry => shapeDefinitions[entry.Value.GetInt32()]);
    }
}
