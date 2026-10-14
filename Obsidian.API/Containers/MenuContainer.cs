using Obsidian.API.BlockEntities;
using Obsidian.API.Inventory;
using Obsidian.API.Plugins;

namespace Obsidian.API;

/// <summary>
/// A plugin's menu (see <see cref="MenuDefinition"/>), opened with <see cref="IPlayer.OpenInventoryAsync"/>, like a
/// NeoForge mod's <c>AbstractContainerMenu</c>: its slots, then the player's main inventory and hotbar, numbered in
/// that order. Players move items in and out of it like a chest's; <see cref="Events.ContainerClickEventArgs"/> lets
/// the plugin cancel or handle their clicks.
/// </summary>
/// <remarks>
/// <para>
/// By default the menu keeps its slots' items itself, without rules. <see cref="Slots"/> keeps them elsewhere instead,
/// in containers or behind item access, each slot with its own rules. Subclass it to decide where shift-clicks move
/// stacks (<see cref="QuickMove"/>), to handle the buttons of a mod's screen (<see cref="ClickButton"/>), to keep it
/// open only while something holds (<see cref="StillValid"/>), to compute its <see cref="DataSlots"/>
/// (<see cref="GetData"/>) and to send modded clients extra data as it opens (<see cref="WriteOpenData"/>). These run
/// inside calls into the plugin that registered the menu type, never once it's unloading.
/// </para>
/// <para>
/// Several players can have the same container open: their clicks take turns, and each sees the others' changes. It
/// closes for its viewers, with <see cref="Events.ContainerClosedEventArgs"/>, when the plugin that registered its
/// menu type unloads; from then until a plugin registers the type again, opening one of its menus does nothing. A menu
/// with a <see cref="BlockEntity"/> closes when the block entity is removed or the player moves out of its reach.
/// </para>
/// </remarks>
public class MenuContainer : BaseContainer
{
    /// <summary>
    /// How many of the player's slots follow the menu's own in its window: the main inventory's 27, then the hotbar's
    /// 9, like vanilla's menus.
    /// </summary>
    public const int PlayerSlotCount = 36;

    // A slot the menu keeps itself, without rules: every slot is, unless Slots says otherwise.
    private static readonly MenuSlot ownSlot = new();

    private readonly MenuSlot[] slots;

    // The containers slots are kept in, each once.
    private readonly BaseContainer[] sources = [];

    private readonly short[] data = [];

    // While a click changes the menu (see BeginStaging), the stacks of its item access slots as the click left them
    // and as they were before it; null otherwise.
    private Dictionary<int, StagedStack>? staged;

    // A menu instance belongs to one generation, even after its stable type is registered again.
    private readonly WeakReference<IPluginContainer>? owner;

    private readonly bool openable;

    private readonly List<AccessTransfer> transfers = [];

    /// <summary>
    /// Creates a container of <paramref name="menu"/>'s type, which keeps its slots' items itself and starts empty.
    /// </summary>
    /// <param name="menu">The menu type.</param>
    /// <param name="title">The title the menu's screen shows.</param>
    public MenuContainer(MenuDefinition menu, ChatMessage title) : base(menu.SlotCount)
    {
        this.Menu = menu;
        this.Title = title;
        this.slots = [.. Enumerable.Repeat(ownSlot, menu.SlotCount)];

        lock (menu.Gate)
        {
            this.openable = menu.IsOpenable;
            this.owner = menu.Owner is IPluginContainer plugin ? new(plugin) : null;
        }
    }

    /// <summary>
    /// The menu type.
    /// </summary>
    public MenuDefinition Menu { get; }

    // Resolves without retaining an unloaded generation, and never treats a cleared owner as host permission.
    internal bool TryGetOwner(out IPluginContainer? plugin)
    {
        lock (this.Menu.Gate)
        {
            plugin = null;
            if (!this.openable || !this.Menu.IsOpenable)
                return false;

            if (this.owner is not null && !this.owner.TryGetTarget(out plugin))
                return false;

            return ReferenceEquals(plugin, this.Menu.Owner);
        }
    }

    /// <summary>
    /// Where each slot's item is kept and the rules players' clicks follow in it, one for each of the type's slots,
    /// in order. By default the menu keeps them all itself, without rules.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// It isn't one for each slot, or a slot is kept in another menu or past the end of its container or item access.
    /// </exception>
    public IReadOnlyList<MenuSlot> Slots
    {
        get => this.slots;
        init
        {
            ArgumentNullException.ThrowIfNull(value);

            if (value.Count != this.Menu.SlotCount)
            {
                throw new ArgumentException(
                    $"{this.Menu} has {this.Menu.SlotCount} slots, not {value.Count}.",
                    nameof(value));
            }

            foreach (var slot in value)
            {
                var size = slot.Container?.Size ?? slot.Items?.SlotCount ?? int.MaxValue;

                if (slot.Container is MenuContainer)
                    throw new ArgumentException("A menu's slot can't be kept in another menu.", nameof(value));

                if (slot.Index >= size)
                    throw new ArgumentException($"Slot {slot.Index} is past the end of its items.", nameof(value));
            }

            this.slots = [.. value];
            this.sources = [.. value.Select(slot => slot.Container).OfType<BaseContainer>().Distinct()];
            this.HasItemAccessSlots = value.Any(slot => slot.Items is not null);
        }
    }

    /// <summary>
    /// The block entity the menu belongs to, like a machine's: the menu closes when it's removed (its block broken or
    /// replaced, its chunk unloaded) and, like vanilla's <c>stillValid</c>, when the player is no longer within their
    /// block reach plus 4 blocks of its block. Null for a menu of nothing in the world.
    /// </summary>
    public BlockEntity? BlockEntity { get; init; }

    /// <summary>
    /// How many data slots the menu has, like vanilla's <c>ContainerData</c>: short values, like a furnace's progress,
    /// that the server sends the players who have it open as they change (see <see cref="GetData"/>).
    /// </summary>
    public int DataSlots
    {
        get => this.data.Length;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);

            this.data = new short[value];
        }
    }

    /// <summary>
    /// Whether some of the menu's slots are kept behind item access.
    /// </summary>
    internal bool HasItemAccessSlots { get; private init; }

    public override long Revision =>
        this.sources.Aggregate(base.Revision, (revision, source) => Math.Max(revision, source.Revision));

    /// <summary>
    /// A data slot's value: by default the last one <see cref="SetData"/> set, or 0. Override it to read the value
    /// from what it shows, like a machine's progress; the server reads it once a tick for each player who has the menu
    /// open and sends it when it changed.
    /// </summary>
    /// <param name="index">The data slot, from 0 to <see cref="DataSlots"/> - 1.</param>
    public virtual short GetData(int index) => this.data[index];

    /// <summary>
    /// Sets a data slot's value, which the players who have the menu open are sent on their next tick.
    /// </summary>
    /// <param name="index">The data slot, from 0 to <see cref="DataSlots"/> - 1.</param>
    /// <param name="value">The value.</param>
    public void SetData(int index, short value) => this.data[index] = value;

    /// <summary>
    /// Where a shift-click moves <paramref name="stack"/> from <paramref name="slot"/>, like vanilla's
    /// <c>quickMoveStack</c>: ranges of the window's slots, tried in turn until one takes some of it. Each tops up
    /// stacks of the same item first, then fills its first empty slot that takes the item, following the slots'
    /// rules. By default, like a chest: the menu's stacks go to the player's slots, filling from the hotbar's last
    /// slot, and the player's go to the menu's slots, filling from the first.
    /// </summary>
    /// <param name="player">The player shift-clicking.</param>
    /// <param name="slot">
    /// The clicked slot, as the window numbers it: the menu's slots, then the player's (see
    /// <see cref="PlayerSlotCount"/>).
    /// </param>
    /// <param name="stack">What the slot holds. Don't change it.</param>
    /// <remarks>
    /// Called with the window's lock held, so that it must not wait on anything. The click repeats it while the stack
    /// moves and the slot still holds the same item. A range that includes <paramref name="slot"/> skips it.
    /// </remarks>
    protected internal virtual IReadOnlyList<MenuSlotRange> QuickMove(IPlayer player, int slot, ItemStack stack) =>
        slot < this.Size
            ? [new MenuSlotRange(this.Size, this.Size + PlayerSlotCount, Reverse: true)]
            : [new MenuSlotRange(0, this.Size)];

    /// <summary>
    /// Handles a button of the menu's screen that the player clicked, like vanilla's <c>clickMenuButton</c>: the
    /// screen sends the button's number, which means whatever the menu says.
    /// </summary>
    /// <param name="player">The player clicking.</param>
    /// <param name="button">The button's number, which the client chose.</param>
    /// <returns>Whether it changed the menu's slots, so that its viewers are sent them again.</returns>
    /// <remarks>
    /// Called with the window's lock held, like a click, so that it may change the slots but must not wait on
    /// anything; only while the menu is still valid for the player and they aren't a spectator.
    /// </remarks>
    protected internal virtual bool ClickButton(IPlayer player, int button) => false;

    /// <summary>
    /// Whether the menu stays open for <paramref name="player"/>, checked every tick and on every click, after its
    /// <see cref="BlockEntity"/>'s checks. True by default.
    /// </summary>
    protected internal virtual bool StillValid(IPlayer player) => true;

    /// <summary>
    /// Writes extra data modded clients get as the menu opens for <paramref name="player"/>, like a NeoForge mod's
    /// <c>openMenu</c> with a buffer: NeoForge clients, and the Obsidian client, read it to build their side of the
    /// menu. Other clients get the menu without it. Nothing by default.
    /// </summary>
    protected internal virtual void WriteOpenData(IPlayer player, INetStreamWriter writer)
    {
    }

    public override ItemStack? GetItem(int slot)
    {
        var binding = this.slots[slot];

        if (binding.Container is BaseContainer container)
            return container.GetItem(binding.Index);

        if (binding.Items is IItemAccess items)
            return this.GetFromItemAccess(slot, items, binding.Index);

        return base.GetItem(slot);
    }

    /// <remarks>
    /// A slot kept behind item access changes only in a click, which moves its items through the access; set it
    /// through the access instead.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The slot is kept behind item access, outside a click.</exception>
    public override void SetItem(int slot, ItemStack? item)
    {
        var binding = this.slots[slot];

        if (binding.Container is BaseContainer container)
            container.SetItem(binding.Index, item);
        else if (binding.Items is IItemAccess items)
            this.Stage(slot, items, binding.Index, item);
        else
            base.SetItem(slot, item);
    }

    public override void Resize(int newSize) =>
        throw new NotSupportedException("A menu has its type's slots.");

    internal bool MayPlace(int slot, ItemStack item) => this.slots[slot].MayPlace?.Invoke(item) ?? true;

    internal bool MayPickup(int slot, IPlayer player) => this.slots[slot].MayPickup?.Invoke(player) ?? true;

    internal int MaxStackSize(int slot, ItemStack item)
    {
        var binding = this.slots[slot];
        var limit = binding.Container?.SlotLimit(binding.Index, item) ?? item.MaxStackSize;

        return Math.Min(binding.MaxStackSize, limit);
    }

    internal override IEnumerable<BaseContainer> LockParts =>
        [this, .. this.sources.SelectMany(source => source.LockParts)];

    internal override bool ShowsRetired =>
        this.IsRetired
        || !this.TryGetOwner(out _)
        || this.sources.Any(source => source.ShowsRetired)
        || this.BlockEntity is { IsRemoved: true }
        || this.BoundInventory is { ShowsRetired: true };

    // A menu of a container block entity closes with the windows showing its inventory, as the block entity goes.
    internal override bool SharesSlotsWith(BaseContainer other) =>
        ReferenceEquals(this, other)
        || this.sources.Any(source => source.SharesSlotsWith(other))
        || this.BoundInventory?.SharesSlotsWith(other) == true;

    internal override ItemStack?[] GetItems() =>
        this.sources.Length == 0 && !this.HasItemAccessSlots ? base.GetItems() : [.. this];

    // A viewer of the menu views the containers it shows too, so that changes to them reach the menu's viewers.
    internal override void AddViewer(IPlayer player)
    {
        base.AddViewer(player);

        foreach (var shown in this.Shown())
            shown.AddViewer(player);
    }

    internal override void RemoveViewer(IPlayer player)
    {
        base.RemoveViewer(player);

        foreach (var shown in this.Shown())
            shown.RemoveViewer(player);
    }

    internal override IPlayer[] GetViewers()
    {
        IEnumerable<IPlayer> viewers = base.GetViewers();

        foreach (var shown in this.Shown())
            viewers = viewers.Union(shown.GetViewers());

        return [.. viewers];
    }

    /// <summary>
    /// Starts a click that may change the item access slots: until <see cref="EndStaging"/>, they change only as the
    /// click sees them, and <see cref="CommitStaged"/> moves the items through the access. Call it with the window's
    /// lock held.
    /// </summary>
    internal void BeginStaging()
    {
        this.staged = [];
        this.transfers.Clear();
    }

    internal void EndStaging()
    {
        this.staged = null;
        this.transfers.Clear();
    }

    /// <summary>
    /// Moves the items a click moved in and out of the item access slots through the access: first what each slot
    /// lost, then what it gained.
    /// </summary>
    /// <param name="leftovers">
    /// Gets the items that didn't go back in: what a slot no longer took, and on failure what was taken out and
    /// couldn't be put back. They're the player's.
    /// </param>
    /// <returns>
    /// False when a slot no longer held what the click took from it: the caller must undo the successful transfers
    /// with <see cref="UndoStaged"/> before restoring the player's stacks and ending staging.
    /// </returns>
    internal bool CommitStaged(List<ItemStack> leftovers)
    {
        var changes = this.staged!
            .Where(entry => Removed(entry.Value) > 0 || Added(entry.Value) > 0)
            .OrderBy(entry => entry.Key)
            .ToArray();
        foreach (var (slot, stack) in changes)
        {
            var count = Removed(stack);
            if (count == 0)
                continue;

            var binding = this.slots[slot];
            var items = binding.Items!;
            var got = ContainerItemAccess.WithoutWaiting(() => items.Extract(binding.Index, count));

            if (got is not null)
                this.transfers.Add(new AccessTransfer(items, binding.Index, got, Inserted: false));

            if (got is null || got.Count != count || !got.Equals(stack.Before))
            {
                return false;
            }
        }

        foreach (var (slot, stack) in changes)
        {
            var count = Added(stack);
            if (count == 0)
                continue;

            var binding = this.slots[slot];
            var items = binding.Items!;
            var added = new ItemStack(stack.After!, count);

            var left = ContainerItemAccess.WithoutWaiting(() => items.Insert(binding.Index, added));
            var accepted = added.Count - (left?.Count ?? 0);
            if (accepted > 0)
            {
                this.transfers.Add(new AccessTransfer(
                    items, binding.Index, new ItemStack(added, accepted), Inserted: true));
            }

            if (left is not null)
                leftovers.Add(left);
        }

        return true;
    }

    // Reverses successful transfers even when a later callback throws. Each access operation is atomic, as its
    // contract requires. A failing recovery must not stop recovery of earlier transfers or delete their items.
    internal void UndoStaged(List<ItemStack> refunds, List<ItemStack> retained)
    {
        refunds.Clear();

        foreach (var transfer in this.transfers.AsEnumerable().Reverse())
        {
            var stack = new ItemStack(transfer.Stack, transfer.Stack.Count);
            if (transfer.Inserted)
            {
                ItemStack? recovered = null;
                try
                {
                    recovered = ContainerItemAccess.WithoutWaiting(
                        () => transfer.Items.Extract(transfer.Index, stack.Count));
                }
                catch
                {
                    // The inserted items still belong to the access: don't restore a second copy to the player.
                }

                if (recovered is not null && recovered.Equals(stack))
                    stack.Count -= recovered.Count;
                else if (recovered is not null)
                    refunds.Add(recovered);

                if (stack.Count > 0)
                    retained.Add(stack);

                continue;
            }

            // An insertion that couldn't be reversed may have come from an earlier extraction in this click.
            ConsumeRetained(stack, retained);
            if (stack.Count == 0)
                continue;

            try
            {
                if (ContainerItemAccess.WithoutWaiting(
                    () => transfer.Items.Insert(transfer.Index, stack)) is ItemStack left)
                {
                    refunds.Add(left);
                }
            }
            catch
            {
                refunds.Add(stack);
            }
        }
    }

    // Cancels items still in an access against the copies rollback would otherwise restore elsewhere.
    internal static void ConsumeRetained(ItemStack stack, List<ItemStack> retained)
    {
        foreach (var kept in retained)
        {
            if (!stack.Equals(kept))
                continue;

            var count = Math.Min(stack.Count, kept.Count);
            stack.Count -= count;
            kept.Count -= count;
        }
    }

    private sealed record AccessTransfer(IItemAccess Items, int Index, ItemStack Stack, bool Inserted);

    // The block entity's inventory, for a menu of a container block entity.
    private BaseContainer? BoundInventory => (this.BlockEntity as ContainerBlockEntity)?.Inventory;

    // The containers the menu shows: those its slots are kept in, and its block entity's inventory.
    private IEnumerable<BaseContainer> Shown() =>
        this.BoundInventory is BaseContainer bound ? this.sources.Append(bound).Distinct() : this.sources;

    private ItemStack? GetFromItemAccess(int slot, IItemAccess items, int index)
    {
        if (this.staged is null)
            return Read(items, index);

        if (this.staged.TryGetValue(slot, out var stack))
            return stack.After;

        var read = Read(items, index);
        this.staged[slot] = new StagedStack(read is null ? null : new ItemStack(read, read.Count), read);

        return read;
    }

    private void Stage(int slot, IItemAccess items, int index, ItemStack? item)
    {
        if (this.staged is null)
            throw new InvalidOperationException("A slot kept behind item access changes through the access.");

        var before = this.staged.TryGetValue(slot, out var stack) ? stack.Before : Read(items, index);
        this.staged[slot] = new StagedStack(before, item);
    }

    // A copy of what the access holds in the slot, without waiting on the click locks the caller may hold.
    private static ItemStack? Read(IItemAccess items, int index) =>
        ContainerItemAccess.WithoutWaiting(() => items.GetItem(index));

    // How many items a click took from a slot: all of them when it holds another item now.
    private static int Removed(StagedStack stack) => stack switch
    {
        { Before: null or { IsAir: true } } => 0,
        { After: null or { IsAir: true } } => stack.Before.Count,
        _ when !stack.After.Equals(stack.Before) => stack.Before.Count,
        _ => Math.Max(0, stack.Before.Count - stack.After.Count)
    };

    // How many items a click put in a slot: all of them when it held another item.
    private static int Added(StagedStack stack) => stack switch
    {
        { After: null or { IsAir: true } } => 0,
        { Before: null or { IsAir: true } } => stack.After.Count,
        _ when !stack.After.Equals(stack.Before) => stack.After.Count,
        _ => Math.Max(0, stack.After.Count - stack.Before.Count)
    };

    // An item access slot's stack before a click (a copy) and as the click left it.
    private readonly record struct StagedStack(ItemStack? Before, ItemStack? After);
}

/// <summary>
/// Where a slot of a plugin's menu keeps its item, and the rules players' clicks follow in it, like vanilla's
/// <c>Slot</c> and NeoForge's <c>SlotItemHandler</c> (see <see cref="MenuContainer.Slots"/>).
/// </summary>
/// <remarks>
/// <para>
/// A slot kept in a container is the container's slot: clicks change it with the container's lock held, like a
/// chest's, and the players who have the container open, on its own or in another menu, see it change. A slot behind
/// <see cref="IItemAccess"/>, like a plugin machine's buffer, moves its items through the access as a click ends:
/// what the click took is extracted first, and if a slot no longer holds it the whole click is undone; then what it
/// put in is inserted, and what the access refuses goes back to the player. The access is called with the menu's
/// locks held, so it must not wait on a container's lock (a container block entity's access doesn't: a busy one
/// takes and gives nothing), and a menu mustn't show a container's slots both ways.
/// </para>
/// <para>
/// The rules are checked as a click moves items, with the window's lock held, inside a call into the plugin; they
/// must not wait on anything. A shift-click follows them too (see <see cref="MenuContainer.QuickMove"/>).
/// </para>
/// </remarks>
public sealed class MenuSlot
{
    /// <summary>
    /// A slot the menu keeps itself.
    /// </summary>
    public MenuSlot()
    {
    }

    /// <summary>
    /// A slot kept in <paramref name="container"/>'s slot <paramref name="index"/>, like a chest's or a block
    /// entity's.
    /// </summary>
    public MenuSlot(BaseContainer container, int index)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        this.Container = container;
        this.Index = index;
    }

    /// <summary>
    /// A slot kept behind <paramref name="items"/>' slot <paramref name="index"/>, like NeoForge's
    /// <c>SlotItemHandler</c>.
    /// </summary>
    public MenuSlot(IItemAccess items, int index)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        this.Items = items;
        this.Index = index;
    }

    /// <summary>
    /// The container the slot is kept in, if it is.
    /// </summary>
    public BaseContainer? Container { get; }

    /// <summary>
    /// The item access the slot is kept behind, if it is.
    /// </summary>
    public IItemAccess? Items { get; }

    /// <summary>
    /// The slot of <see cref="Container"/> or <see cref="Items"/> that the menu's slot shows.
    /// </summary>
    public int Index { get; }

    /// <summary>
    /// Whether players may put a stack of an item in the slot, like vanilla's <c>mayPlace</c>; anything when null.
    /// A slot that takes nothing, like a machine's output, still gives its stack whole to a cursor of the same item.
    /// </summary>
    public Func<ItemStack, bool>? MayPlace { get; init; }

    /// <summary>
    /// Whether a player may take the slot's stack, like vanilla's <c>mayPickup</c>; always when null.
    /// </summary>
    public Func<IPlayer, bool>? MayPickup { get; init; }

    /// <summary>
    /// The most items the slot holds, up to each item's own stack size, like vanilla's <c>getMaxStackSize</c>: 99 by
    /// default, so that the item's stack size decides. A container-backed slot also respects its owning
    /// <see cref="ContainerBlockEntity"/>'s slot limit.
    /// </summary>
    public int MaxStackSize
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 99);

            field = value;
        }
    } = 99;
}

/// <summary>
/// A range of a menu window's slots that a shift-click moves a stack into (see <see cref="MenuContainer.QuickMove"/>),
/// like the bounds vanilla's <c>moveItemStackTo</c> takes.
/// </summary>
/// <param name="Start">The first slot, as the window numbers them.</param>
/// <param name="End">The slot after the last.</param>
/// <param name="Reverse">Whether the range is filled from its last slot.</param>
public readonly record struct MenuSlotRange(int Start, int End, bool Reverse = false);
