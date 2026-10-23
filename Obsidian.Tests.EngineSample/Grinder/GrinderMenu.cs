using Obsidian.API;
using Obsidian.API.Inventory;

namespace EngineSample.Grinder;

/// <summary>
/// Phase 3's menu, <c>sample:grinder</c>, which a player opens by using the grinder: its input and output slots,
/// bound to its block entity, data slots for its progress and mode, a button toggling its mode, and the recipes it
/// grinds, sent as the menu opens for its screen (<see cref="GrinderScreen"/>) to list.
/// </summary>
/// <remarks>
/// The numbers of its data slots and button are the menu's own: its screen, in the same assembly, uses the same
/// constants.
/// </remarks>
public sealed class GrinderMenu : MenuContainer
{
    public const string Identifier = "sample:grinder";

    /// <summary>
    /// The data slot with the ticks spent grinding the input's item.
    /// </summary>
    public const int ProgressData = 0;

    /// <summary>
    /// The data slot with the ticks the input's item takes to grind, 0 while nothing grinds.
    /// </summary>
    public const int TimeData = 1;

    /// <summary>
    /// The data slot with the grinder's mode: 1 while it pulls from above, 0 when it doesn't.
    /// </summary>
    public const int PullsData = 2;

    /// <summary>
    /// The button that turns pulling from above on or off.
    /// </summary>
    public const int TogglePulls = 0;

    private readonly GrinderBlockEntity grinder;

    public GrinderMenu(MenuDefinition menu, GrinderBlockEntity grinder)
        : base(menu, new ChatMessage { Translate = "container.sample.grinder" })
    {
        this.grinder = grinder;

        // The grinder's own container, so players and the grinder take turns with its slots. Players put in only
        // what grinds, and only take from the output.
        this.Slots =
        [
            new MenuSlot(grinder.Inventory, GrinderBlockEntity.InputSlot) { MayPlace = grinder.Grinds },
            new MenuSlot(grinder.Inventory, GrinderBlockEntity.OutputSlot) { MayPlace = _ => false }
        ];
        this.BlockEntity = grinder;
        this.DataSlots = 3;
    }

    // Read from the block entity each tick for the players who have the menu open, who are sent what changed.
    public override short GetData(int index) => index switch
    {
        ProgressData => (short)this.grinder.Progress,
        TimeData => (short)this.grinder.Time,
        _ => (short)(this.grinder.Pulls ? 1 : 0)
    };

    // The default shift-clicks suit it: the menu's stacks go to the player, and the player's to the input when it
    // takes them, since the output takes nothing.
    protected override bool ClickButton(IPlayer player, int button)
    {
        if (button == TogglePulls)
            this.grinder.Pulls = !this.grinder.Pulls;

        return false;
    }

    // Each recipe as its ingredient and its result, which the screen lists in two columns.
    protected override void WriteOpenData(IPlayer player, INetStreamWriter writer)
    {
        var recipes = this.grinder.Server?.GetRecipes(GrinderBlock.Recipes) ?? [];

        writer.WriteVarInt(recipes.Count);
        foreach (var recipe in recipes)
        {
            writer.WriteRequiredItemStack(new ItemStack(recipe.Ingredient));
            writer.WriteRequiredItemStack(new ItemStack(recipe.Result, recipe.Count));
        }
    }
}
