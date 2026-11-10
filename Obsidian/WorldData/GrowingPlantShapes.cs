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
/// scheduling read it: kelp converting returns before vanilla schedules its water tick.
/// </remarks>
internal static class GrowingPlantShapes
{
    // A body turning into a head takes vanilla's GrowingPlantHeadBlock.getStateForPlacement age, nextInt(25).
    private const int PlacementAges = 25;

    /// <summary>
    /// Whether the update from <paramref name="neighbor"/> in <paramref name="direction"/> turns
    /// <paramref name="state"/> into its plant's other part.
    /// </summary>
    internal static bool Converts(Func<Vector, IBlock> getBlock, Vector position, IBlock state, BlockFace direction,
        IBlock neighbor)
    {
        if (PlantOf(state.RegistryId) is not Plant plant)
            return false;

        if (state.RegistryId == plant.Body)
            return direction == plant.Growth && !plant.Contains(neighbor);

        if (direction == plant.Growth)
            return plant.Contains(neighbor);

        // From the support side, a head that can stay checks the block ahead of it instead.
        return direction == plant.Growth.Opposite()
            && CanSurvive(getBlock, position, plant)
            && plant.Contains(getBlock(position.Offset(plant.Growth)));
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
    internal static bool NeedsSupportCheck(Func<Vector, IBlock> getBlock, Vector position, IBlock state,
        BlockFace direction) =>
        PlantOf(state.RegistryId) is Plant plant
        && direction == plant.Growth.Opposite()
        && !CanSurvive(getBlock, position, plant);

    // Vanilla's GrowingPlantBlock.canSurvive: on its own plant, or on a sturdy face (never magma, for kelp).
    private static bool CanSurvive(Func<Vector, IBlock> getBlock, Vector position, Plant plant)
    {
        var support = getBlock(position.Offset(plant.Growth.Opposite()));
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
