using Obsidian.API;
using Obsidian.API.Client.Screens;
using Obsidian.API.Inventory;

namespace EngineSample.Grinder;

/// <summary>
/// Phase 3's screen built in code, the client's side of <see cref="GrinderMenu"/>: the grinder's input and output
/// slots with a progress arrow between them, a button toggling its mode, and the recipes it grinds, which a search
/// field filters and a scroll bar scrolls. It's drawn with vanilla's GUI textures, so the sample ships none.
/// </summary>
/// <remarks>
/// <para>
/// The sample registers the same screen built in Razor, <c>GrinderView.razor</c>; this one shows the code form. To
/// use it instead, register it with <c>registry.RegisterMenuScreen(GrinderMenu.Identifier, menu => new
/// GrinderScreen(menu))</c>.
/// </para>
/// <para>
/// The client makes one each time the server opens the menu, and calls <see cref="Init"/> each time it's shown, after
/// removing its widgets: the widgets it keeps are fields, whose events are handled once, in the constructor.
/// </para>
/// </remarks>
public sealed class GrinderScreen : ModMenuScreen
{
    // Vanilla's chest texture, whose rows of slot frames, 18 pixels apart, frame the slots and the recipe list.
    private const string ChestTexture = "minecraft:gui/container/generic_54";

    // The recipe list: each row a recipe, its ingredient then its result.
    private readonly ItemGrid list = new()
    {
        X = 118,
        Y = 18,
        Columns = 2,
        Rows = 3
    };

    private readonly ScrollBar scroll = new()
    {
        X = 156,
        Y = 18,
        Height = 54,
        Handle = "minecraft:container/stonecutter/scroller"
    };

    private readonly TextField search = new()
    {
        X = 117,
        Y = 4,
        Width = 52,
        Height = 12
    };

    // The furnace's arrow, filling as the input grinds.
    private readonly ProgressBar progress = new()
    {
        X = 47,
        Y = 34,
        Width = 24,
        Height = 16,
        Source = GuiImage.Sprite("minecraft:container/furnace/burn_progress")
    };

    private readonly Button mode = new()
    {
        X = 8,
        Y = 54,
        Width = 104,
        Height = 16
    };

    // The recipes the menu opened with, each as its ingredient and its result.
    private readonly ItemStack[][] recipes;

    public GrinderScreen(IScreenMenu menu) : base(menu)
    {
        this.recipes = ReadRecipes(menu.ReadOpenData());
        this.search.Changed += this.Search;
        this.scroll.Scrolled += row => this.list.FirstRow = row;
        this.mode.Pressed += () => this.Menu.ClickButton(GrinderMenu.TogglePulls);
    }

    protected override void Init()
    {
        this.Background = GuiImage.Sprite("minecraft:recipe_book/overlay_recipe");

        // The slots' frames, then the slots: the input, the output and, by default, the player's inventory.
        this.Add(Frames(25, 35, columns: 1, rows: 1));
        this.Add(Frames(77, 35, columns: 1, rows: 1));
        this.Add(Frames(8, 84, columns: 9, rows: 3));
        this.Add(Frames(8, 142, columns: 9, rows: 1));
        this.Add(new SlotGrid
        {
            X = 25,
            Y = 35,
            FirstSlot = GrinderBlockEntity.InputSlot
        });
        this.Add(new SlotGrid
        {
            X = 77,
            Y = 35,
            FirstSlot = GrinderBlockEntity.OutputSlot
        });

        // The furnace's empty arrow under the progress that fills it.
        this.Add(new Image
        {
            X = this.progress.X,
            Y = this.progress.Y,
            Width = this.progress.Width,
            Height = this.progress.Height,
            Source = GuiImage.Texture("minecraft:gui/container/furnace", 79, 34)
        });
        this.Add(this.progress);
        this.Add(this.mode);

        // The search over the list, and the stonecutter's scroll bar track beside it.
        this.search.Hint = this.Translate("sample.grinder.search");
        this.Add(this.search);
        this.Add(Frames(this.list.X, this.list.Y, this.list.Columns, this.list.Rows));
        this.Add(this.list);
        this.Add(new Image
        {
            X = this.scroll.X - 1,
            Y = this.scroll.Y - 1,
            Width = 14,
            Height = 56,
            Source = GuiImage.Texture("minecraft:gui/container/stonecutter", 118, 14)
        });
        this.scroll.WheelArea = this.list;
        this.Add(this.scroll);

        this.Search(this.search.Text);
        this.Tick();
    }

    // The data slots, as the server last sent them: the progress and the mode.
    protected override void Tick()
    {
        var time = this.Menu.GetData(GrinderMenu.TimeData);
        this.progress.Progress = time > 0 ? this.Menu.GetData(GrinderMenu.ProgressData) / (float)time : 0;

        var pulls = this.Menu.GetData(GrinderMenu.PullsData) != 0;
        this.mode.Text = this.Translate(pulls ? "sample.grinder.pulls.on" : "sample.grinder.pulls.off");
    }

    // Shows the recipes whose ingredient or result has the text in its identifier ("raw iron" finds
    // minecraft:raw_iron), from the top.
    private void Search(string text)
    {
        var name = text.Trim().Replace(' ', '_');

        bool Matches(ItemStack stack) =>
            stack.Holder.UnlocalizedName.Contains(name, StringComparison.OrdinalIgnoreCase);

        this.list.Items = [.. this.recipes.Where(recipe => recipe.Any(Matches)).SelectMany(recipe => recipe)];

        var rows = (this.list.Items.Count + this.list.Columns - 1) / this.list.Columns;
        this.scroll.MaxRow = rows - this.list.Rows;
        this.scroll.Row = 0;
        this.list.FirstRow = 0;
    }

    // Reads what GrinderMenu.WriteOpenData wrote: the count, then each recipe's ingredient and result. GrinderView
    // reads it too.
    internal static ItemStack[][] ReadRecipes(INetStreamReader reader)
    {
        var recipes = new ItemStack[reader.ReadVarInt()][];
        for (var i = 0; i < recipes.Length; i++)
            recipes[i] = [reader.ReadRequiredItemStack(), reader.ReadRequiredItemStack()];

        return recipes;
    }

    // Vanilla's slot frames around a grid whose first item is at (x, y), from the chest texture's rows, which have up
    // to 9 by 6 of them.
    private static Image Frames(int x, int y, int columns, int rows) => new()
    {
        X = x - 1,
        Y = y - 1,
        Width = columns * 18,
        Height = rows * 18,
        Source = GuiImage.Texture(ChestTexture, 7, 17)
    };
}
