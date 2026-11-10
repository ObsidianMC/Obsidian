using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData;

/// <summary>
/// The shape updates of kelp and the cave, weeping and twisting vines (vanilla's <c>GrowingPlantHeadBlock</c> and
/// <c>GrowingPlantBodyBlock</c>): a head turns into its body once its own plant stands ahead of it, and a body whose
/// plant ahead is gone turns back into a head. Checked against the <c>growing_plant_ticks</c> capture.
/// </summary>
/// <remarks>
/// <see cref="Converts"/> draws nothing, so both the shape update (<see cref="KelpBlockBehavior"/>) and the fluid
/// scheduling read it: kelp converting returns before vanilla schedules its water tick. The predicates take the blocks
/// they read rather than a way to read them, since they run on every shape update of these plants: an update from the
/// support side carries the support block as its neighbour, and only the block ahead may need reading (see
/// <see cref="BlockAheadToRead"/>).
/// </remarks>
internal static class GrowingPlantShapes
{
    // A body turning into a head takes vanilla's GrowingPlantHeadBlock.getStateForPlacement age, nextInt(25).
    private const int PlacementAges = 25;

    /// <summary>
    /// Where the block <see cref="Converts"/> needs as its block ahead is, or null when it needs none: only a head
    /// updated from its support side looks ahead of it.
    /// </summary>
    internal static Vector? BlockAheadToRead(Vector position, IBlock state, BlockFace direction) =>
        PlantOf(state.RegistryId) is Plant plant
        && state.RegistryId == plant.Head
        && direction == plant.Growth.Opposite()
            ? position.Offset(plant.Growth)
            : null;

    /// <summary>
    /// Whether the update from <paramref name="neighbor"/> in <paramref name="direction"/> turns
    /// <paramref name="state"/> into its plant's other part. <paramref name="ahead"/> is the block at
    /// <see cref="BlockAheadToRead"/>, when that names one.
    /// </summary>
    internal static bool Converts(IBlock state, BlockFace direction, IBlock neighbor, IBlock? ahead)
    {
        if (PlantOf(state.RegistryId) is not Plant plant)
            return false;

        if (state.RegistryId == plant.Body)
            return direction == plant.Growth && !plant.Contains(neighbor);

        if (direction == plant.Growth)
            return plant.Contains(neighbor);

        // From the support side, which the neighbour is, a head that can stay checks the block ahead of it instead.
        return direction == plant.Growth.Opposite()
            && ahead is not null
            && CanSurvive(neighbor, plant)
            && plant.Contains(ahead);
    }

    /// <summary>
    /// The part <paramref name="state"/> converts to (see <see cref="Converts"/>): the body, or a head with an age
    /// drawn from <paramref name="random"/>, the level's. Cave vines keep their berries either way.
    /// </summary>
    internal static IBlock Convert(IBlock state, IRandomSource random)
    {
        var plant = PlantOf(state.RegistryId)!.Value;
        var converted = state.RegistryId == plant.Head
            ? BlocksRegistry.GetDefault(plant.Body)
            : BlocksRegistry.GetDefault(plant.Head).WithProperty("age", random.NextInt(PlacementAges));

        return plant.Head == BlockIds.CaveVines
            ? converted.WithProperty("berries", state.GetProperty("berries") == "true")
            : converted;
    }

    /// <summary>
    /// Whether the update schedules the plant's support check: it came from the support side, and the plant can't
    /// stay there.
    /// </summary>
    internal static bool NeedsSupportCheck(IBlock state, BlockFace direction, IBlock neighbor) =>
        PlantOf(state.RegistryId) is Plant plant
        && direction == plant.Growth.Opposite()
        && !CanSurvive(neighbor, plant);

    // Vanilla's GrowingPlantBlock.canSurvive, given the block behind the plant: its own plant, or a sturdy face (never
    // magma, for kelp).
    private static bool CanSurvive(IBlock support, Plant plant)
    {
        if (plant.Head == BlockIds.Kelp && support.Is(BlockIds.MagmaBlock))
            return false;

        return plant.Contains(support) || support.IsFaceSturdy(plant.Growth);
    }

    private static Plant? PlantOf(int block) => block switch
    {
        BlockIds.Kelp or BlockIds.KelpPlant => new Plant(BlockIds.Kelp, BlockIds.KelpPlant, BlockFace.Up),
        BlockIds.CaveVines or BlockIds.CaveVinesPlant =>
            new Plant(BlockIds.CaveVines, BlockIds.CaveVinesPlant, BlockFace.Down),
        BlockIds.WeepingVines or BlockIds.WeepingVinesPlant =>
            new Plant(BlockIds.WeepingVines, BlockIds.WeepingVinesPlant, BlockFace.Down),
        BlockIds.TwistingVines or BlockIds.TwistingVinesPlant =>
            new Plant(BlockIds.TwistingVines, BlockIds.TwistingVinesPlant, BlockFace.Up),
        _ => null
    };

    private readonly record struct Plant(int Head, int Body, BlockFace Growth)
    {
        public bool Contains(IBlock block) => block.RegistryId == this.Head || block.RegistryId == this.Body;
    }
}
