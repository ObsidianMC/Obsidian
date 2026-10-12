using Obsidian.API.Registry.Codecs.Biomes;

namespace Obsidian.WorldData;

/// <summary>
/// The charges of the mobs around, for biomes' spawn costs: a mob whose biome gives its type a cost only spawns where
/// the potential energy its charge would add stays within the cost's energy budget, so such mobs spread out. Each
/// spawned mob with a cost adds its charge.
/// </summary>
internal sealed class SpawnPotential
{
    private readonly List<(Vector Position, double Charge)> charges = [];

    /// <summary>
    /// Adds a mob's charge at its block position.
    /// </summary>
    public void Add(Vector position, double charge)
    {
        if (charge != 0)
            this.charges.Add((position, charge));
    }

    /// <summary>
    /// Whether a mob with the cost may spawn at the block position.
    /// </summary>
    public bool Allows(Vector position, SpawnCost cost) =>
        this.EnergyChange(position, cost.Charge) <= cost.EnergyBudget;

    // Vanilla's PotentialCalculator formula, which decides where vanilla's mobs with costs (soul sand valleys' and
    // warped forests') spawn: each charge's potential is the charge over its distance, infinite at its own block, and
    // the change is the sum of the potentials times the new charge.
    private double EnergyChange(Vector position, double charge)
    {
        if (charge == 0)
            return 0;

        var potential = 0.0;

        foreach (var (at, existing) in this.charges)
        {
            double dx = at.X - position.X;
            double dy = at.Y - position.Y;
            double dz = at.Z - position.Z;
            var distanceSquared = dx * dx + dy * dy + dz * dz;

            potential += distanceSquared == 0
                ? double.PositiveInfinity
                : existing / Math.Sqrt(distanceSquared);
        }

        return potential * charge;
    }
}
