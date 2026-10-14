using Obsidian.API;
using Obsidian.API.BlockEntities;
using Obsidian.API.Inventory;
using Obsidian.API.World;
using Obsidian.Nbt;

namespace EngineSample.Grinder;

/// <summary>
/// The grinder's block entity: a container with an input slot and an output slot, the ticks spent grinding the input
/// and whether it pulls from above, saved with its chunk. Each tick it pulls an item it can grind from the container
/// above (unless its menu's button turned that off), grinds, and pushes what it made into whatever is below.
/// </summary>
/// <remarks>
/// Other blocks reach its slots too, through item access: the top and sides reach the input, which takes only what
/// grinds, and the bottom reaches the output, which only the grinder fills. So a hopper above can feed it, and one
/// below can empty it.
/// </remarks>
public sealed class GrinderBlockEntity() : ContainerBlockEntity(new Slots())
{
    public const int InputSlot = 0;
    public const int OutputSlot = 1;

    private static readonly int[] input = [InputSlot];
    private static readonly int[] output = [OutputSlot];

    // Changed only on the level's tick, like the rest of the block entity's own state.
    private int progress;
    private int time;

    // Changed by a click in the grinder's menu, on the player's thread, and read on the level's tick: a field written
    // whole, which the tick sees once it's set.
    private volatile bool pulls = true;

    /// <summary>
    /// The ticks spent grinding the input's item, up to its recipe's time.
    /// </summary>
    public int Progress => this.progress;

    /// <summary>
    /// The ticks the input's item takes to grind, by its recipe; 0 while nothing grinds.
    /// </summary>
    public int Time => this.time;

    /// <summary>
    /// Whether the grinder pulls items from the container above; when it doesn't, only players and hoppers fill it.
    /// Its menu's button toggles it.
    /// </summary>
    public bool Pulls
    {
        get => this.pulls;
        set
        {
            this.pulls = value;
            this.SetChanged();
        }
    }

    protected override void Load(NbtCompound tag)
    {
        base.Load(tag);
        this.progress = tag.TryGetTagValue<int>("progress", out var progress) ? progress : 0;
        this.pulls = !tag.TryGetTagValue<byte>("pulls", out var pulls) || pulls != 0;
    }

    protected override void Save(NbtCompound tag)
    {
        // The container saves its items; the grinder adds its progress and its mode, a bool saved as a byte like
        // vanilla's.
        base.Save(tag);
        tag.Add(new NbtTag<int>("progress", this.progress));
        tag.Add(new NbtTag<byte>("pulls", (byte)(this.pulls ? 1 : 0)));
    }

    /// <summary>
    /// Whether the grinder has a recipe for <paramref name="stack"/>, which is what its input takes.
    /// </summary>
    public bool Grinds(ItemStack stack) => this.FindRecipe(stack) is not null;

    protected override IReadOnlyList<int> GetSlots(BlockFace? side) => side switch
    {
        null => base.GetSlots(side),
        BlockFace.Down => output,
        _ => input
    };

    // Called with the inventory's lock held: finding a recipe reads the server's recipes without waiting on anything.
    protected override bool CanInsert(int slot, ItemStack stack, BlockFace? side) =>
        slot == InputSlot ? this.Grinds(stack) : side is null;

    protected override bool CanExtract(int slot, ItemStack stack, BlockFace? side) =>
        side is null || slot == OutputSlot;

    // A block entity ticks outside the level's tick lock, which block hooks hold: item access here may wait for a
    // player's click to finish, and a chest above still waiting for its loot table rolls it first, as for a hopper.
    protected override ValueTask TickAsync()
    {
        if (this.Level is not ILevel level || this.GetItemAccess(side: null) is not IItemAccess own)
            return ValueTask.CompletedTask;

        if (this.pulls)
            this.Pull(level, own);

        this.Grind(own);
        this.Push(level, own);

        return ValueTask.CompletedTask;
    }

    // Pulls one item from the container above, through its bottom face: the first the input takes.
    private void Pull(ILevel level, IItemAccess own)
    {
        if (level.GetItemAccess(this.BlockPosition + Vector.Up, BlockFace.Down) is not IItemAccess above)
            return;

        for (var slot = 0; slot < above.SlotCount; slot++)
        {
            // Planned on both ends before either changes.
            if (above.Extract(slot, 1, simulate: true) is not ItemStack planned
                || own.Insert(InputSlot, planned, simulate: true) is not null)
            {
                continue;
            }

            if (above.Extract(slot, 1) is ItemStack taken && own.Insert(InputSlot, taken) is ItemStack refused)
            {
                // A player's click swapped the chest's slot between the plan and the move, and the input doesn't
                // take what came out: it goes back where it came from or, if another click filled that, anywhere in
                // the chest.
                if (above.Insert(slot, refused) is ItemStack left)
                    above.Insert(left);
            }

            return;
        }
    }

    // Grinds the input's item a tick further. Once its recipe's time is up and the output has room, one of it turns
    // into the recipe's result; until then it waits at full progress.
    private void Grind(IItemAccess own)
    {
        var recipe = own.GetItem(InputSlot) is ItemStack item ? this.FindRecipe(item) : null;
        this.time = recipe?.Time ?? 0;
        if (recipe is null)
        {
            this.SetProgress(0);
            return;
        }

        this.SetProgress(Math.Min(this.progress + 1, recipe.Time));
        if (this.progress < recipe.Time)
            return;

        var result = new ItemStack(recipe.Result, recipe.Count);
        result.Set(GrinderBlock.GroundBy, this.BlockPosition);

        // Only the grinder takes from its input and fills its output, so once planned, the real calls do the same.
        if (own.Insert(OutputSlot, result, simulate: true) is not null)
            return;

        own.Extract(InputSlot, 1);
        own.Insert(OutputSlot, result);
        this.SetProgress(0);
    }

    // Pushes as much of the output as fits into whatever is below, through its top face.
    private void Push(ILevel level, IItemAccess own)
    {
        if (level.GetItemAccess(this.BlockPosition + Vector.Down, BlockFace.Up) is not IItemAccess below
            || own.Extract(OutputSlot, int.MaxValue, simulate: true) is not ItemStack planned)
        {
            return;
        }

        // Planned on both ends before either changes.
        var fits = planned.Count - (below.Insert(planned, simulate: true)?.Count ?? 0);
        if (fits > 0 && own.Extract(OutputSlot, fits) is ItemStack taken && below.Insert(taken) is ItemStack refused)
        {
            // A click filled the block below between the plan and the move: the output, which only the grinder
            // fills, takes back what it refused.
            own.Insert(OutputSlot, refused);
        }
    }

    // The recipe that grinds the stack, or null when nothing does, found through the plugin's own server. Recipes go
    // when the plugin unloads and come back from its files when it loads again, so it's looked up each time.
    private GrindingRecipe? FindRecipe(ItemStack stack) => this.Server?.FindRecipe(GrinderBlock.Recipes, [stack]);

    // Saved at the end of the tick when it changes.
    private void SetProgress(int progress)
    {
        if (progress == this.progress)
            return;

        this.progress = progress;
        this.SetChanged();
    }

    // A two-slot inventory: vanilla's generic containers come in rows of nine.
    private sealed class Slots() : BaseContainer(2);
}
