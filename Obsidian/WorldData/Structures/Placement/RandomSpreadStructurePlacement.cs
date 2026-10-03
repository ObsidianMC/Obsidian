using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Structures.Placement;

/// <summary>
/// One start per grid cell of <see cref="Spacing"/> chunks, at a random spot keeping <see cref="Separation"/> chunks from
/// the next cell, like vanilla's <c>RandomSpreadStructurePlacement</c>.
/// </summary>
[StructureType("minecraft:random_spread")]
public sealed class RandomSpreadStructurePlacement : StructurePlacement
{
    public required int Spacing { get; init; }

    public required int Separation { get; init; }

    public RandomSpreadType SpreadType { get; init; } = RandomSpreadType.Linear;

    /// <summary>
    /// Vanilla <c>getPotentialStructureChunk</c>: the start chunk of the grid cell containing the chunk.
    /// </summary>
    public (int X, int Z) GetPotentialStructureChunk(long seed, int chunkX, int chunkZ)
    {
        var cellX = Mth.FloorDiv(chunkX, this.Spacing);
        var cellZ = Mth.FloorDiv(chunkZ, this.Spacing);

        var random = new WorldgenRandom(new LegacyRandomSource(0L));
        random.SetLargeFeatureWithSalt(seed, cellX, cellZ, this.Salt);

        var range = this.Spacing - this.Separation;
        var offsetX = this.Evaluate(random, range);
        var offsetZ = this.Evaluate(random, range);
        return (cellX * this.Spacing + offsetX, cellZ * this.Spacing + offsetZ);
    }

    private protected override bool IsPlacementChunk(IStructurePlacementState state, int chunkX, int chunkZ) =>
        this.GetPotentialStructureChunk(state.Seed, chunkX, chunkZ) == (chunkX, chunkZ);

    private int Evaluate(WorldgenRandom random, int range) => this.SpreadType == RandomSpreadType.Triangular
        ? (random.NextInt(range) + random.NextInt(range)) / 2
        : random.NextInt(range);
}

/// <summary>
/// Distribution of the start inside its grid cell, like vanilla's <c>RandomSpreadType</c>.
/// </summary>
public enum RandomSpreadType
{
    Linear,

    /// <summary>Averages two draws, favoring the middle of the cell.</summary>
    Triangular
}
