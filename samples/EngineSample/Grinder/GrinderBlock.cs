using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Crafting;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Plugins;
using Obsidian.Nbt;

namespace EngineSample.Grinder;

/// <summary>
/// Phase 2's machine, <c>sample:grinder</c>. Its block entity (<see cref="GrinderBlockEntity"/>) pulls items from the
/// container above, grinds them by <c>sample:grinding</c> recipes (<see cref="GrindingRecipe"/>, read from
/// <c>data/sample/recipe/grinding/</c>) and pushes the results into whatever is below, stamped with the grinder that
/// made them (<see cref="GroundBy"/>). Breaking it drops it through its loot table and its items as a container's.
/// </summary>
/// <remarks>
/// What it registers is held in statics of the plugin's own: a reload loads a new copy of the plugin, which registers
/// everything again and sets its own.
/// </remarks>
public static class GrinderBlock
{
    public const string Identifier = "sample:grinder";

    /// <summary>
    /// The registered block, whose item the block's properties give it by default.
    /// </summary>
    public static BlockType Block { get; private set; } = null!;

    /// <summary>
    /// The <c>sample:grinding</c> recipe type.
    /// </summary>
    public static RecipeType<GrindingRecipe> Recipes { get; private set; } = null!;

    /// <summary>
    /// <c>sample:ground_by</c>: the position of the grinder that made a stack.
    /// </summary>
    public static PluginComponentType<Vector> GroundBy { get; private set; } = null!;

    // The plugin's own server, which finds its recipes.
    private static IServer server = null!;

    /// <summary>
    /// Registers the recipe type, the component type, the block (with its item) and its block entity type.
    /// </summary>
    /// <param name="registry">The registry <c>ConfigureRegistry</c> is given.</param>
    /// <param name="pluginServer">The plugin's server, injected before <c>ConfigureRegistry</c>.</param>
    public static void Register(IPluginRegistry registry, IServer pluginServer)
    {
        server = pluginServer;
        Recipes = registry.RegisterRecipeType("sample:grinding", new GrindingRecipeSerializer());

        // Without a network codec the component is the server's own: it's saved with stacks but never sent to
        // clients, which don't need to know it.
        GroundBy = registry.RegisterComponentType(
            "sample:ground_by",
            new ComponentCodec<Vector>(SavePosition, LoadPosition));

        Block = registry.RegisterBlock(Identifier, new BlockProperties { Hardness = 3.5f });
        registry.RegisterBlockEntity<GrinderBlockEntity>(Identifier, Block);
    }

    /// <summary>
    /// The recipe that grinds <paramref name="stack"/>, or null when nothing does. Recipes go when the plugin unloads
    /// and come back from its files when it loads again, so it's looked up each time rather than kept.
    /// </summary>
    public static GrindingRecipe? FindRecipe(ItemStack stack) => server.FindRecipe(Recipes, [stack]);

    // Saved as vanilla saves a block position: an int array of x, y and z.
    private static INbtTag SavePosition(Vector position) =>
        new NbtArray<int>("", [position.X, position.Y, position.Z]);

    private static Vector LoadPosition(INbtTag tag)
    {
        var xyz = ((NbtArray<int>)tag).GetArray();

        return new Vector(xyz[0], xyz[1], xyz[2]);
    }
}
