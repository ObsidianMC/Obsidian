using Obsidian.Nbt;
using Obsidian.WorldData.Structures;

namespace Obsidian.WorldData.Generators.Mojang.Structures;

/// <summary>Saved start metadata needed for references, without retaining its pieces or the rest of its chunk NBT.</summary>
internal readonly record struct SavedStructureStart(string Identifier, BlockBox PieceBounds)
{
    public static SavedStructureStart[] Read(NbtCompound? starts)
    {
        if (starts is null || starts.Count == 0)
            return [];

        var result = new List<SavedStructureStart>();
        foreach (var (identifier, tag) in starts)
        {
            if (tag is NbtCompound saved && Read(identifier, saved) is SavedStructureStart start)
                result.Add(start);
        }

        return [.. result];
    }

    public static SavedStructureStart? Read(string identifier, NbtCompound saved)
    {
        if (saved.GetString("id") == "INVALID" || !saved.TryGetTag<NbtList>("Children", out var children))
            return null;

        BlockBox? bounds = null;
        foreach (var child in children)
        {
            if (child is not NbtCompound piece || !piece.TryGetTag<NbtArray<int>>("BB", out var box)
                || box.Count != 6)
            {
                continue;
            }

            var next = BlockBox.Create(box[0], box[1], box[2], box[3], box[4], box[5]);
            bounds = bounds is BlockBox previous ? previous.Encapsulate(next) : next;
        }

        return bounds is BlockBox found ? new SavedStructureStart(identifier, found) : null;
    }
}
