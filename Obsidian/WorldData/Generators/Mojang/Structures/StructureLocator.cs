using Obsidian.WorldData.Structures;
using Obsidian.WorldData.Structures.Placement;

namespace Obsidian.WorldData.Generators.Mojang.Structures;

/// <summary>
/// Finds the nearest structure of a set of structures, like vanilla's <c>ChunkGenerator.findNearestMapStructure</c> (explorer
/// maps, eyes of ender, <c>/locate</c>).
/// </summary>
internal sealed class StructureLocator(StructureManager structures)
{
    /// <summary>
    /// The locate position (<see cref="StructurePlacement.LocateOffset"/> from the start chunk's corner) of the nearest start of
    /// <paramref name="targets"/>, or <c>null</c> when none is found.
    /// </summary>
    /// <param name="radius">For spread placements, how many rings of placement regions to search around the position.</param>
    /// <param name="skipKnownStructures">Skips starts already found by an earlier search (they're found once each).</param>
    public Vector? FindNearest(IReadOnlyCollection<Structure> targets, Vector position, int radius, bool skipKnownStructures)
    {
        // Each placement with the target structures it places, in structure set order.
        var placements = new List<(StructurePlacement Placement, Structure[] Structures)>();
        foreach (var set in structures.Sets)
        {
            var placed = set.Structures.Select(entry => entry.Structure).Where(targets.Contains).ToArray();
            if (placed.Length > 0)
                placements.Add((set.Placement, placed));
        }

        if (placements.Count == 0)
            return null;

        Vector? best = null;
        var bestDistance = double.MaxValue;
        var spreads = new List<(RandomSpreadStructurePlacement Placement, Structure[] Structures)>();

        foreach (var (placement, placed) in placements)
        {
            if (placement is ConcentricRingsStructurePlacement rings)
            {
                var found = this.FindNearestInRings(placed, position, skipKnownStructures, rings);
                if (found is not null)
                    Consider(found.Value);
            }
            else if (placement is RandomSpreadStructurePlacement spread)
            {
                spreads.Add((spread, placed));
            }
        }

        if (spreads.Count == 0)
            return best;

        var chunkX = position.X >> 4;
        var chunkZ = position.Z >> 4;
        for (var ring = 0; ring <= radius; ring++)
        {
            var foundAny = false;
            foreach (var (placement, placed) in spreads)
            {
                var found = this.FindInSpreadRing(placed, chunkX, chunkZ, ring, skipKnownStructures, placement);
                if (found is null)
                    continue;

                foundAny = true;
                Consider(found.Value);
            }

            if (foundAny)
                return best;
        }

        return best;

        void Consider(Vector found)
        {
            var distance = DistanceSquared(found, position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = found;
            }
        }
    }

    /// <summary>
    /// Vanilla <c>getNearestGeneratedStructure</c> for rings: the ring positions closest first, checked until one has a start.
    /// </summary>
    private Vector? FindNearestInRings(Structure[] placed, Vector position, bool skipKnown, ConcentricRingsStructurePlacement placement)
    {
        Vector? best = null;
        var bestDistance = double.MaxValue;
        foreach (var (x, z) in structures.GetRingPositions(placement))
        {
            var distance = DistanceSquared(new Vector((x << 4) + 8, 32, (z << 4) + 8), position);
            if (best is not null && distance >= bestDistance)
                continue;

            var found = this.FindStartAt(placed, skipKnown, placement, x, z);
            if (found is not null)
            {
                best = found;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>
    /// Vanilla <c>getNearestGeneratedStructure</c> for spread placements: the first start on the edge of the square of regions
    /// <paramref name="ring"/> away, west to east then north to south.
    /// </summary>
    private Vector? FindInSpreadRing(Structure[] placed, int chunkX, int chunkZ, int ring, bool skipKnown, RandomSpreadStructurePlacement placement)
    {
        for (var dx = -ring; dx <= ring; dx++)
        {
            var edgeX = dx == -ring || dx == ring;
            for (var dz = -ring; dz <= ring; dz++)
            {
                if (!edgeX && dz != -ring && dz != ring)
                    continue;

                var (x, z) = placement.GetPotentialStructureChunk(structures.Seed, chunkX + placement.Spacing * dx, chunkZ + placement.Spacing * dz);
                var found = this.FindStartAt(placed, skipKnown, placement, x, z);
                if (found is not null)
                    return found;
            }
        }

        return null;
    }

    /// <summary>
    /// Vanilla <c>getStructureGeneratingAt</c>: the locate position of a target start in the chunk.
    /// </summary>
    private Vector? FindStartAt(Structure[] placed, bool skipKnown, StructurePlacement placement, int chunkX, int chunkZ)
    {
        if (!placement.IsStructureChunk(structures, chunkX, chunkZ))
            return null;

        foreach (var start in structures.GetStarts(chunkX, chunkZ))
        {
            if (!placed.Contains(start.Structure))
                continue;

            // References are saved with the start, so its saved state comes first, and its chunk stays loaded to save the new one.
            if (skipKnown)
            {
                structures.LoadStart(start);
                if (!start.TryAddReference())
                    continue;
            }

            return new Vector(chunkX << 4, 0, chunkZ << 4) + placement.LocateOffset;
        }

        return null;
    }

    private static double DistanceSquared(Vector a, Vector b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        double dz = a.Z - b.Z;
        return dx * dx + dy * dy + dz * dz;
    }
}
