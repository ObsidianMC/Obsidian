using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Client;
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
/// Using it opens its menu (<see cref="GrinderMenu"/>). On the client, its model is
/// <c>assets/sample/models/block/grinder.json</c>, and a block entity renderer spins its wheel while it grinds (see
/// <see cref="RegisterClient"/>).
/// </summary>
/// <remarks>
/// What it registers is held in statics of the plugin's own: a reload loads a new copy of the plugin, which registers
/// everything again and sets its own. Its block entity finds recipes through its own <c>Server</c>, the plugin's.
/// </remarks>
public sealed class GrinderBlock : IBlockBehavior
{
    public const string Identifier = "sample:grinder";

    // The wheel its renderer spins: assets/sample/models/part/grinder_wheel.json.
    private const string WheelModel = "sample:part/grinder_wheel";

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

    /// <summary>
    /// The <c>sample:grinder</c> menu type.
    /// </summary>
    public static MenuDefinition Menu { get; private set; } = null!;

    /// <summary>
    /// Registers the recipe type, the component type, the block (with its item), its block entity type and its menu
    /// type. A mod registers its content on both sides, so the client knows the menu type too.
    /// </summary>
    /// <param name="registry">The registry <c>ConfigureRegistry</c> is given.</param>
    public static void Register(IPluginRegistry registry)
    {
        Recipes = registry.RegisterRecipeType("sample:grinding", new GrindingRecipeSerializer());

        // Without a network codec the component is the server's own: it's saved with stacks but never sent to
        // clients, which don't need to know it.
        GroundBy = registry.RegisterComponentType(
            "sample:ground_by",
            new ComponentCodec<Vector>(SavePosition, LoadPosition));

        Block = registry.RegisterBlock(Identifier, new BlockProperties
        {
            Hardness = 3.5f,

            // Its model is lower than a block, with its wheel on top, so it doesn't hide its neighbours' faces.
            Opaque = false,
            Behavior = new GrinderBlock()
        });
        registry.RegisterBlockEntity<GrinderBlockEntity>(Identifier, Block);

        // Its menu's two slots, the input and the output, in a row.
        Menu = registry.RegisterMenu(GrinderMenu.Identifier, rows: 1, columns: 2);
    }

    /// <summary>
    /// Registers the grinder's client side: its wheel, drawn by a block entity renderer on top of the block's model,
    /// spinning about its centre while the grinder grinds. The block entity's update data says since when (see
    /// <see cref="GrinderBlockEntity.GetUpdateData"/>); the client turns the wheel each frame from that, without
    /// calling the mod.
    /// </summary>
    /// <param name="registry">The registry <c>ConfigureClient</c> is given.</param>
    public static void RegisterClient(IClientRegistry registry)
    {
        registry.RegisterBlockEntityRenderer(Identifier, new BlockEntityRendererProperties
        {
            Models = [WheelModel],
            Provider = (data, block) => [Wheel(data)]
        });
    }

    /// <summary>
    /// Opens the grinder's menu for the player who used it.
    /// </summary>
    public async ValueTask<bool> UseAsync(BlockUseContext context)
    {
        if (await context.Player.Level.GetBlockEntityAsync(context.Position) is not GrinderBlockEntity grinder)
            return false;

        await context.Player.OpenInventoryAsync(new GrinderMenu(Menu, grinder));

        return true;
    }

    // The wheel, still, or turning half a turn a second (9 degrees a tick) about the vertical axis through the
    // block's centre from the game tick it started grinding.
    private static BlockEntityRenderPart Wheel(IReadOnlyDictionary<string, object?> data) =>
        data.TryGetValue("working_since", out var value) && value is long since
            ? new BlockEntityRenderPart
            {
                Model = new BlockModelPart(WheelModel),
                Speed = 9,
                Since = since
            }
            : new BlockEntityRenderPart { Model = new BlockModelPart(WheelModel) };

    // Saved as vanilla saves a block position: an int array of x, y and z.
    private static INbtTag SavePosition(Vector position) =>
        new NbtArray<int>("", [position.X, position.Y, position.Z]);

    private static Vector LoadPosition(INbtTag tag)
    {
        var xyz = ((NbtArray<int>)tag).GetArray();

        return new Vector(xyz[0], xyz[1], xyz[2]);
    }
}
