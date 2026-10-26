using System.Collections.Immutable;
using System.Threading;
using Obsidian.API.BlockEntities;
using Obsidian.API.Blocks;
using Obsidian.API.Containers;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Nbt;
using Obsidian.Registries;

namespace Obsidian.WorldData;

/// <summary>
/// A beacon's block entity, like vanilla's <c>BeaconBlockEntity</c>: every 80 game ticks it measures its pyramid and
/// whether its beam reaches the sky, and while both hold it gives the players around it its powers.
/// </summary>
internal sealed class BeaconBlockEntity : BlockEntity
{
    // How often a beacon checks itself and gives its powers (vanilla's 80 game ticks).
    private const int UpdateInterval = 80;

    private const int MaxLevels = 4;

    // The powers each pyramid level adds, like vanilla's BeaconBlockEntity.BEACON_EFFECTS.
    internal static readonly int[][] EffectsByLevel =
    [
        [(int)PotionEffect.Speed - 1, (int)PotionEffect.Haste - 1],
        [(int)PotionEffect.Resistance - 1, (int)PotionEffect.JumpBoost - 1],
        [(int)PotionEffect.Strength - 1],
        [(int)PotionEffect.Regeneration - 1]
    ];

    private NbtCompound others = new();

    /// <summary>
    /// How many levels of its pyramid count: 0 to 4, or 0 when its beam doesn't reach the sky.
    /// </summary>
    public int Levels { get; private set; }

    /// <summary>
    /// The effect ids of its primary and secondary powers, if chosen.
    /// </summary>
    public int? Primary { get; set; }

    public int? Secondary { get; set; }

    internal override bool SavesOnAnyThread => true;

    /// <summary>
    /// Vanilla's <c>filterEffect</c>: only a power some pyramid level gives can be chosen.
    /// </summary>
    public static bool IsValidEffect(int effect) => EffectsByLevel.Any(level => level.Contains(effect));

    protected internal override void Load(NbtCompound tag)
    {
        var others = DataBlockEntity.Copy(tag);
        others.Remove("primary_effect");
        others.Remove("secondary_effect");

        this.others = others;
        this.Primary = ReadEffect(tag, "primary_effect");
        this.Secondary = ReadEffect(tag, "secondary_effect");
    }

    protected internal override void Save(NbtCompound tag)
    {
        foreach (var (name, child) in DataBlockEntity.Copy(Volatile.Read(ref this.others)))
            tag.Add(name, child);

        if (this.Primary is int primary)
            tag.Add(new NbtTag<string>("primary_effect", EffectName(primary)));

        if (this.Secondary is int secondary)
            tag.Add(new NbtTag<string>("secondary_effect", EffectName(secondary)));
    }

    // Vanilla's getUpdateTag, saveCustomOnly: the powers and the name.
    protected internal override NbtCompound? GetUpdateData()
    {
        var data = new NbtCompound();
        this.Save(data);
        data.Remove("lock");
        return data;
    }

    protected internal override async ValueTask TickAsync()
    {
        if (this.Level is not AbstractLevel level || level.LevelData.Time % UpdateInterval != 0)
            return;

        var position = this.BlockPosition;
        var wasActive = this.Levels > 0;
        var beam = BeamReachesSky(level, position);
        this.Levels = beam ? PyramidLevels(level, position) : 0;

        if (this.Levels > 0)
        {
            this.ApplyEffects(level, position);
            PlaySound(level, position, "minecraft:block.beacon.ambient");
        }

        if (!wasActive && this.Levels > 0)
            PlaySound(level, position, "minecraft:block.beacon.activate");
        else if (wasActive && this.Levels == 0)
            PlaySound(level, position, "minecraft:block.beacon.deactivate");

        // Its windows show its levels.
        foreach (var viewer in level.Players.Values)
        {
            if (viewer.OpenedContainer is BeaconContainer menu && menu.BlockPosition == position)
                await Events.MainEventHandler.RefreshViewersAsync(menu);
        }
    }

    // Vanilla's updateBase: each layer under it, a square one block wider each side than the layer above, counts while
    // it's all beacon base blocks.
    private static int PyramidLevels(AbstractLevel level, Vector position)
    {
        var levels = 0;
        for (var layer = 1; layer <= MaxLevels; layer++)
        {
            var y = position.Y - layer;
            if (y < level.MinY)
                break;

            for (var x = position.X - layer; x <= position.X + layer; x++)
            {
                for (var z = position.Z - layer; z <= position.Z + layer; z++)
                {
                    if (!TagsRegistry.Block.BeaconBaseBlocks.Entries.Contains(level.BlockUpdates.GetBlock(new Vector(x, y, z)).RegistryId))
                        return levels;
                }
            }

            levels = layer;
        }

        return levels;
    }

    // Vanilla's beam sections: the beam stops at a block that blocks all light, bedrock aside.
    private static bool BeamReachesSky(AbstractLevel level, Vector position)
    {
        var top = level.MinY + level.Height;
        for (var y = position.Y + 1; y < top; y++)
        {
            var block = level.BlockUpdates.GetBlock(new Vector(position.X, y, position.Z));
            if (block.LightBlock() >= 15 && block.RegistryId != BlockIds.Bedrock)
                return false;
        }

        return true;
    }

    // Vanilla's applyEffects: players within 10 blocks a level, from that far below it up to the top of the world,
    // get its powers for 9 seconds and 2 a level; a level 4 beacon whose powers are the same gives the primary at
    // level II, otherwise its secondary too.
    private void ApplyEffects(AbstractLevel level, Vector position)
    {
        if (this.Primary is not int primary)
            return;

        var range = this.Levels * 10 + 10;
        var amplifier = this.Levels >= MaxLevels && this.Primary == this.Secondary ? 1 : 0;
        var duration = (9 + this.Levels * 2) * 20;
        const EntityEffectFlags flags = EntityEffectFlags.IsAmbient | EntityEffectFlags.ShowParticles | EntityEffectFlags.ShowIcon;

        foreach (var player in level.Players.Values)
        {
            var at = player.Position;
            if (at.X < position.X - range || at.X > position.X + 1 + range
                || at.Z < position.Z - range || at.Z > position.Z + 1 + range
                || at.Y < position.Y - range)
            {
                continue;
            }

            player.AddPotionEffect(primary, duration, amplifier, flags);
            if (this.Levels >= MaxLevels && this.Secondary is int secondary && secondary != primary)
                player.AddPotionEffect(secondary, duration, 0, flags);
        }
    }

    private static void PlaySound(AbstractLevel level, Vector position, string sound) =>
        level.BlockUpdates.PlaySound(position, sound);

    private static int? ReadEffect(NbtCompound tag, string name)
    {
        if (!tag.TryGetTagValue<string>(name, out var id))
            return null;

        var path = id.StartsWith("minecraft:", StringComparison.Ordinal) ? id["minecraft:".Length..] : id;
        return Enum.TryParse<PotionEffect>(path.Replace("_", ""), ignoreCase: true, out var effect) && IsValidEffect((int)effect - 1)
            ? (int)effect - 1
            : null;
    }

    private static string EffectName(int effect) => $"minecraft:{((PotionEffect)(effect + 1)).ToString().ToSnakeCase()}";
}

/// <summary>
/// A lectern's block entity, like vanilla's <c>LecternBlockEntity</c>: the book on it and the page it's open at.
/// Hoppers can't reach the book.
/// </summary>
internal sealed class LecternBlockEntity : VanillaContainerBlockEntity
{
    // Vanilla's LevelEvent.SOUND_BOOK_PAGE_TURN... played by the client for the reader; others hear it.
    private const int PageTurnEvent = 1043;

    public LecternBlockEntity(Vector position) : base(new LecternContainer { BlockPosition = position }, "Book", "Page")
    {
    }

    public LecternContainer Lectern => (LecternContainer)this.Inventory;

    public override IItemAccess? GetItemAccess(BlockFace? side) => null;

    protected internal override void Load(NbtCompound tag)
    {
        base.Load(tag);

        if (tag.TryGetTag<NbtCompound>("Book", out var book) && book.ItemFromNbt() is { Count: > 0 } item)
            this.Lectern.SetItem(LecternContainer.BookSlot, new ItemStack(item, 1));

        this.Lectern.Page = tag.TryGetTagValue<int>("Page", out var page) ? Math.Max(0, page) : 0;
    }

    protected internal override void Save(NbtCompound tag)
    {
        base.Save(tag);

        // Like vanilla's, the book is saved on its own, not as a container's items.
        tag.Remove("Items");
        if (this.Lectern.Book is ItemStack book)
        {
            tag.Add(book.ToNbt("Book"));
            tag.Add(new NbtTag<int>("Page", this.Lectern.Page));
        }
    }

    /// <summary>
    /// How many pages the lectern's book has (vanilla's <c>getPageCount</c>).
    /// </summary>
    public static int PageCount(ItemStack? book)
    {
        if (book is null)
            return 0;

        if (book.GetComponent<WrittenBookDataComponent>(DataComponentType.WrittenBookContent) is { } written)
            return written.Pages.Length;

        return book.GetComponent<SimpleDataComponent<ImmutableArray<Page>>>(DataComponentType.WritableBookContent)?.Value.Length ?? 0;
    }

    /// <summary>
    /// Vanilla's <c>getRedstoneSignal</c>: 0 without a book, else 1 to 15 by how far through it the page is.
    /// </summary>
    public int AnalogSignal()
    {
        if (this.Lectern.Book is not ItemStack book)
            return 0;

        var pages = PageCount(book);
        var share = pages > 1 ? this.Lectern.Page / (pages - 1f) : 1f;
        return (int)MathF.Floor(share * 14) + 1;
    }

    /// <summary>
    /// Opens the book at <paramref name="page"/>, like vanilla's <c>setPage</c>: a page that changes turns with a short
    /// redstone pulse.
    /// </summary>
    /// <returns>Whether the page changed.</returns>
    public async ValueTask<bool> SetPageAsync(int page)
    {
        var pages = PageCount(this.Lectern.Book);
        var clamped = Math.Clamp(page, 0, Math.Max(0, pages - 1));
        if (clamped == this.Lectern.Page)
            return false;

        this.Lectern.Page = clamped;
        this.SetChanged();

        if (this.Level is AbstractLevel level)
        {
            await Redstone.RedstoneLectern.TurnPageAsync(level, this.BlockPosition);
            level.BlockUpdates.UpdateAnalogOutput(this.BlockPosition);

            level.BroadcastLevelEvent(PageTurnEvent, this.BlockPosition, 0);
        }

        return true;
    }

    /// <summary>
    /// Puts <paramref name="book"/> on the lectern, like vanilla's <c>LecternBlock.placeBook</c>.
    /// </summary>
    public void PlaceBook(ItemStack book)
    {
        this.Lectern.SetItem(LecternContainer.BookSlot, new ItemStack(book, 1));
        this.Lectern.Page = 0;
        this.SetChanged();
        this.SetHasBook(true);

        if (this.Level is AbstractLevel level)
            level.BlockUpdates.PlaySound(this.BlockPosition, "minecraft:item.book.put");
    }

    /// <summary>
    /// Takes the book off the lectern, like vanilla's <c>LecternMenu</c>'s take button.
    /// </summary>
    public ItemStack? TakeBook()
    {
        if (this.Lectern.Book is not ItemStack book)
            return null;

        this.Lectern.SetItem(LecternContainer.BookSlot, null);
        this.Lectern.Page = 0;
        this.SetChanged();
        this.SetHasBook(false);

        return book;
    }

    // Vanilla's resetBookState: the block shows the book, and the block under it hears of it.
    private void SetHasBook(bool hasBook)
    {
        if (this.Level is not AbstractLevel level)
            return;

        lock (level.Fluids.TickLock)
        {
            var updates = level.BlockUpdates;
            var position = this.BlockPosition;
            var state = updates.GetBlock(position);
            if (state.RegistryId != BlockIds.Lectern)
                return;

            var changed = state.WithProperty("has_book", hasBook).WithProperty("powered", false);
            updates.Set(position, changed, BlockUpdateFlags.All);
            updates.UpdateNeighbors(position + new Vector(0, -1, 0), changed);
            updates.UpdateAnalogOutput(position);
        }
    }
}

/// <summary>
/// A crafter's block entity, like vanilla's <c>CrafterBlockEntity</c>: its grid with its disabled slots, whether it's
/// powered, and the ticks it shows itself crafting.
/// </summary>
internal sealed class CrafterBlockEntity : VanillaContainerBlockEntity
{
    public CrafterBlockEntity(Vector position)
        : base(new CrafterContainer { BlockPosition = position }, "disabled_slots", "triggered", "crafting_ticks_remaining")
    {
    }

    public CrafterContainer Crafter => (CrafterContainer)this.Inventory;

    protected internal override void Load(NbtCompound tag)
    {
        base.Load(tag);

        var crafter = this.Crafter;
        for (var slot = 0; slot < CrafterContainer.GridSize; slot++)
            crafter.SetSlotDisabled(slot, false);

        if (tag.TryGetTag<NbtArray<int>>("disabled_slots", out var disabled))
        {
            foreach (var slot in disabled.GetArray())
                crafter.SetSlotDisabled(slot, true);
        }

        crafter.Triggered = tag.TryGetTagValue<int>("triggered", out var triggered) && triggered == 1;
        crafter.CraftingTicksRemaining = tag.TryGetTagValue<int>("crafting_ticks_remaining", out var ticks) ? ticks : 0;
    }

    protected internal override void Save(NbtCompound tag)
    {
        base.Save(tag);

        var crafter = this.Crafter;
        var disabled = Enumerable.Range(0, CrafterContainer.GridSize).Where(crafter.IsSlotDisabled).ToArray();
        tag.Add(new NbtArray<int>("disabled_slots", disabled));
        tag.Add(new NbtTag<int>("triggered", crafter.Triggered ? 1 : 0));
        tag.Add(new NbtTag<int>("crafting_ticks_remaining", crafter.CraftingTicksRemaining));
    }

    /// <summary>
    /// Vanilla's <c>canPlaceItem</c>: not in a disabled slot, nor a full one; and not on a stack while a later slot is
    /// empty or holds less of the item, so that hoppers spread items over the grid.
    /// </summary>
    protected internal override bool CanInsert(int slot, ItemStack stack, BlockFace? side)
    {
        var crafter = this.Crafter;
        if (crafter.IsSlotDisabled(slot))
            return false;

        var current = crafter.GetItem(slot);
        if (current.IsNullOrAir())
            return true;

        if (current.Count >= current.MaxStackSize)
            return false;

        for (var other = slot + 1; other < CrafterContainer.GridSize; other++)
        {
            if (crafter.IsSlotDisabled(other))
                continue;

            var item = crafter.GetItem(other);
            if (item.IsNullOrAir() || item.Count < current.Count && item == current)
                return false;
        }

        return true;
    }

    // Vanilla's serverTick: the crafter stops showing itself crafting when its ticks are up.
    protected internal override ValueTask TickAsync()
    {
        var crafter = this.Crafter;
        if (crafter.CraftingTicksRemaining <= 0 || this.Level is not AbstractLevel level)
            return ValueTask.CompletedTask;

        crafter.CraftingTicksRemaining--;
        if (crafter.CraftingTicksRemaining == 0)
        {
            lock (level.Fluids.TickLock)
            {
                var state = level.BlockUpdates.GetBlock(this.BlockPosition);
                if (state.RegistryId == BlockIds.Crafter)
                    level.BlockUpdates.Set(this.BlockPosition, state.WithProperty("crafting", false), BlockUpdateFlags.All);
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Vanilla's <c>getRedstoneSignal</c>: how many slots hold something or are disabled.
    /// </summary>
    public int AnalogSignal() => Enumerable.Range(0, CrafterContainer.GridSize)
        .Count(slot => !this.Crafter.GetItem(slot).IsNullOrAir() || this.Crafter.IsSlotDisabled(slot));
}
