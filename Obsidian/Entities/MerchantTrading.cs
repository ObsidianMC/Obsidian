using Obsidian.API.Inventory;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Packets.Play.Serverbound;

namespace Obsidian.Entities;

internal static class MerchantTrading
{
    internal static async ValueTask OpenAsync(Mob merchant, IPlayer player, ImmutableArray<TradeEntry> offers,
        int level, int experience, bool canRestock, Func<TradeEntry, MerchantTradeUpdate>? completed = null)
    {
        if (!merchant.Alive || player.Health <= 0 || player.GameMode == GameMode.Spectator ||
            merchant.Level != player.Level || !merchant.IsInRange(player, 4) || offers.IsEmpty ||
            merchant.Level.GetPlayersInRange(merchant.Position, 16).Any(other =>
                other.OpenedContainer is MerchantContainer menu && ReferenceEquals(menu.Merchant, merchant)))
            return;
        var container = new MerchantContainer(merchant, player, offers, level, experience, canRestock, completed);
        await player.OpenInventoryAsync(container);
        await container.SyncAsync();
    }
}

internal readonly record struct MerchantTradeUpdate(ImmutableArray<TradeEntry> Offers, int Level, int Experience);

internal sealed class MerchantContainer : BaseContainer
{
    internal Mob Merchant { get; }
    private readonly IPlayer customer;
    private ImmutableArray<TradeEntry> offers;
    private readonly bool canRestock;
    private readonly Func<TradeEntry, MerchantTradeUpdate>? completed;
    private readonly System.Threading.SemaphoreSlim clicks = new(1, 1);
    private int level;
    private int experience;
    private int selected;
    private int state;
    private bool closed;

    internal MerchantContainer(Mob merchant, IPlayer player, ImmutableArray<TradeEntry> offers, int level,
        int experience, bool canRestock, Func<TradeEntry, MerchantTradeUpdate>? completed)
        : base(3, InventoryType.Merchant)
    {
        Merchant = merchant;
        customer = player;
        this.offers = offers;
        this.level = level;
        this.experience = experience;
        this.canRestock = canRestock;
        this.completed = completed;
        var defaultTitle = merchant.Type == EntityType.Villager ? "Villager" : "Wandering Trader";
        Title = merchant.CustomName ?? ChatMessage.Simple(defaultTitle);
    }

    internal async ValueTask SelectAsync(int index)
    {
        await using var transaction = DeferredInventoryWork.Begin(this.CustomerLogger);
        await clicks.WaitAsync();
        try
        {
            if (!ReferenceEquals(customer.OpenedContainer, this) || customer.Health <= 0)
                return;

            if (closed || index < 0 || index >= offers.Length || !Merchant.Alive
                || Merchant.Level != customer.Level || !Merchant.IsInRange(customer, 8))
                return;
            selected = index;
            ReturnInputs();
            var offer = offers[index];
            FillInput(0, offer.FirstInput);
            FillInput(1, offer.SecondInput);
            await SyncAsync();
        }
        finally
        {
            clicks.Release();
        }
    }

    // Matches vanilla's MerchantOffer.getModifiedCostCount: the base count, plus its demand times the price
    // multiplier (in single precision, floored, never negative), plus the special price, within the item's stack size.
    private static int Cost(TradeEntry offer)
    {
        var baseCount = offer.FirstInput.Count;
        var demandBonus = Math.Max(0, (int)MathF.Floor(baseCount * offer.Demand * offer.Multiplier));
        var maximum = Math.Max(1, ItemsRegistry.Get(offer.FirstInput.ID).MaxStackSize);
        return Math.Clamp(baseCount + demandBonus + offer.Discount, 1, maximum);
    }

    private static bool Matches(ItemStack? stack, TradeItem required) => required.Count <= 0
        || stack is not null && stack.Holder.Id == required.ID
        && required.Components.All(component =>
            stack.TryGetComponent(component.Type, out var actual) && Equals(actual, component));

    private bool CanTrade(TradeEntry offer) => !offer.IsDisabled && offer.UsedCount < offer.MaxCount &&
        Matches(items[0], offer.FirstInput) && items[0]?.Count >= Cost(offer) &&
        Matches(items[1], offer.SecondInput)
        && (offer.SecondInput.Count <= 0 || items[1]?.Count >= offer.SecondInput.Count);

    private void FillInput(int slot, TradeItem required)
    {
        if (required.Count <= 0)
            return;
        for (var index = 9; index <= 44; index++)
        {
            var stack = customer.Inventory.GetItem(index);
            if (!Matches(stack, required) || stack == null)
                continue;
            var amount = Math.Min(stack.Count, stack.MaxStackSize - (items[slot]?.Count ?? 0));
            if (amount == 0)
                break;
            if (items[slot] == null)
                items[slot] = new ItemStack(stack, amount);
            else if (items[slot] == stack)
                items[slot]!.Count += amount;
            else
                continue;
            customer.Inventory.RemoveItem(index, amount);
        }
    }

    internal async ValueTask HandleClickAsync(ContainerClickPacket packet, IPlayer player)
    {
        await using var transaction = DeferredInventoryWork.Begin(this.CustomerLogger);
        await clicks.WaitAsync();
        try
        {
            if (!ReferenceEquals(player.OpenedContainer, this) || player.Health <= 0)
                return;

            if (closed || player != customer || packet.ContainerId != player.CurrentContainerId)
                return;
            if (!Merchant.Alive || Merchant.Level != player.Level || !Merchant.IsInRange(player, 8))
            {
                await CloseUnderLockAsync(player, notify: true);
                return;
            }
            if (packet.StateId != state)
            {
                await SyncAsync();
                return;
            }
            var slot = packet.ClickedSlot;
            if (slot == 2 && (packet.ClickType == ClickType.QuickMove
                || packet.ClickType == ClickType.Pickup && packet.Button is 0 or 1))
                TakeResult(packet.ClickType == ClickType.QuickMove);
            else if (slot is >= 0 and < 39 && packet.ClickType == ClickType.Pickup && packet.Button is 0 or 1)
            {
                var mapped = GetSlot(slot);
                BaseContainer source = mapped.ForPlayer ? customer.Inventory : this;
                var stack = source.GetItem(mapped.Slot);
                var carried = customer.CarriedItem;
                if (carried == null || carried.IsAir || carried.Count <= 0)
                {
                    if (stack != null)
                    {
                        var amount = packet.Button == 1 ? (stack.Count + 1) / 2 : stack.Count;
                        customer.CarriedItem = new ItemStack(stack, amount);
                        source.RemoveItem(mapped.Slot, amount);
                    }
                }
                else if (stack == null || stack.IsAir || stack.Count <= 0)
                {
                    var amount = packet.Button == 1 ? 1 : carried.Count;
                    source.SetItem(mapped.Slot, new ItemStack(carried, amount));
                    carried.Count -= amount;
                }
                else if (stack == carried)
                {
                    var amount = Math.Min(packet.Button == 1 ? 1 : carried.Count, stack.MaxStackSize - stack.Count);
                    stack.Count += amount;
                    carried.Count -= amount;
                }
                else if (packet.Button == 0)
                {
                    source.SetItem(mapped.Slot, carried);
                    customer.CarriedItem = stack;
                }
                if (customer.CarriedItem is { Count: <= 0 })
                    customer.CarriedItem = null;
            }
            else if (slot is >= 0 and < 39 && packet.ClickType == ClickType.QuickMove)
            {
                if (slot < 2)
                {
                    if (items[slot] is ItemStack input)
                    {
                        StoreInInventory(input);
                        if (input.Count <= 0)
                            items[slot] = null;
                    }
                }
                else
                {
                    var mapped = GetSlot(slot);
                    var stack = customer.Inventory.GetItem(mapped.Slot);
                    if (stack is not null)
                    {
                        for (var input = 0; input < 2; input++)
                        {
                            var required = input == 0 ? offers[selected].FirstInput : offers[selected].SecondInput;
                            if (required.Count <= 0 || !Matches(stack, required)
                                || items[input] is not null && items[input] != stack)
                            {
                                continue;
                            }

                            var amount = Math.Min(stack.Count, stack.MaxStackSize - (items[input]?.Count ?? 0));
                            if (items[input] is null)
                                items[input] = new ItemStack(stack, amount);
                            else
                                items[input]!.Count += amount;

                            customer.Inventory.RemoveItem(mapped.Slot, amount);
                            break;
                        }
                    }
                }
            }
            await SyncAsync();
        }
        finally
        {
            clicks.Release();
        }
    }

    private void TakeResult(bool quickMove)
    {
        var offer = offers[selected];
        while (CanTrade(offer))
        {
            var result = new ItemStack(offer.Output, offer.Output.Count);
            if (quickMove)
            {
                if (InventoryCapacity(result) < result.Count)
                    break;
                StoreInInventory(result);
            }
            else
            {
                var cursor = customer.CarriedItem;
                if (cursor is not null && !cursor.IsAir
                    && (cursor != result || cursor.Count + result.Count > cursor.MaxStackSize))
                    break;
                if (cursor == null || cursor.IsAir)
                    customer.CarriedItem = result;
                else
                    cursor.Count += result.Count;
            }
            RemoveItem(0, Cost(offer));
            if (offer.SecondInput.Count > 0)
                RemoveItem(1, offer.SecondInput.Count);
            offer.UsedCount++;
            offer.IsDisabled = offer.UsedCount >= offer.MaxCount;

            // Villagers draw before rewarding their career even with rewardExp false. Wandering traders draw only
            // when rewardExp is true, matching their separate vanilla rewardTradeXp implementations.
            var playerExperience = 0;
            if (canRestock || offer.RewardExp)
                playerExperience = 3 + Merchant.Random.Next(4);

            var previousLevel = level;
            if (completed is not null)
            {
                var update = completed(offer);
                offers = update.Offers;
                level = update.Level;
                experience = update.Experience;
            }
            else
            {
                experience += offer.XP;
            }

            if (canRestock && level > previousLevel)
                playerExperience += 5;

            if (offer.RewardExp)
            {
                var position = Merchant.Position + new VectorD(0, 0.5, 0);
                Merchant.Level.SpawnExperienceOrbs(position, (short)playerExperience);
            }

            if (!quickMove)
                break;
        }
    }

    private int InventoryCapacity(ItemStack result)
    {
        var capacity = 0;
        for (var index = 9; index <= 44; index++)
        {
            var stack = customer.Inventory.GetItem(index);
            if (stack is null)
                capacity += result.MaxStackSize;
            else if (stack == result)
                capacity += stack.MaxStackSize - stack.Count;
        }

        return capacity;
    }

    private void StoreInInventory(ItemStack stack)
    {
        for (var pass = 0; pass < 2 && stack.Count > 0; pass++)
        {
            for (var index = 9; index <= 44 && stack.Count > 0; index++)
            {
                var target = customer.Inventory.GetItem(index);
                if (pass == 0 && target == stack && target is not null)
                {
                    var amount = Math.Min(stack.Count, target.MaxStackSize - target.Count);
                    target.Count += amount;
                    stack.Count -= amount;
                }
                else if (pass == 1 && (target is null || target.IsAir || target.Count <= 0))
                {
                    var amount = Math.Min(stack.Count, stack.MaxStackSize);
                    customer.Inventory.SetItem(index, new ItemStack(stack, amount));
                    stack.Count -= amount;
                }
            }
        }
    }

    private void ReturnInputs()
    {
        for (var slot = 0; slot < 2; slot++)
        {
            if (items[slot] is not ItemStack stack)
                continue;

            StoreInInventory(stack);
            if (stack.Count > 0)
            {
                Merchant.Level.SpawnEntity(new ItemEntity
                {
                    Level = Merchant.Level,
                    EntityId = Server.GetNextEntityId(),
                    Position = customer.Position,
                    Item = stack
                });
            }

            items[slot] = null;
        }
    }

    internal async ValueTask CloseAsync(IPlayer player, bool notify = true)
    {
        await using var transaction = DeferredInventoryWork.Begin(this.CustomerLogger);
        await clicks.WaitAsync();
        try
        {
            await CloseUnderLockAsync(player, notify);
        }
        finally
        {
            clicks.Release();
        }
    }

    private async ValueTask CloseUnderLockAsync(IPlayer player, bool notify)
    {
        if (closed)
            return;

        // Like vanilla's removed menu, recover both inputs before the window is detached or the player is saved.
        closed = true;
        ReturnInputs();
        if (!ReferenceEquals(player.OpenedContainer, this))
            return;

        player.OpenedContainer = null;
        if (notify)
        {
            await player.QueuePacketAsync(new Obsidian.Net.Packets.Play.Clientbound.ContainerClosePacket
            {
                ContainerId = player.CurrentContainerId
            });
        }

        await player.QueuePacketAsync(new ContainerSetContentPacket(0, player.Inventory.ToList())
        {
            CarriedItem = player.CarriedItem
        });
    }

    /// <summary>
    /// Runs <paramref name="send"/> in turn with the customer's clicks, unless the window closed, with the state id
    /// their client last got, which their next click must carry to be applied (see <see cref="HandleClickAsync"/>).
    /// </summary>
    /// <remarks>
    /// For a whole window sent outside a click (see MainEventHandler.RefreshStaleWindowAsync). Vanilla advances the id
    /// with every whole update, but its client takes whatever id it's sent, so resending the current one keeps the
    /// client and the server agreeing even when the update is left out as obsolete or a plugin cancels it.
    /// </remarks>
    internal async ValueTask SendInTurnAsync(Func<int, ValueTask> send)
    {
        await using var transaction = DeferredInventoryWork.Begin(this.CustomerLogger);
        await clicks.WaitAsync();
        try
        {
            if (closed)
                return;

            await send(state);
        }
        finally
        {
            clicks.Release();
        }
    }

    internal async ValueTask SyncAsync()
    {
        var offer = offers[selected];
        items[2] = CanTrade(offer) ? new ItemStack(offer.Output, offer.Output.Count) : null;
        var inventory = Enumerable.Range(9, 36).Select(index => customer.Inventory.GetItem(index));
        var contents = items.Concat(inventory).ToList();
        await customer.QueuePacketAsync(new ContainerSetContentPacket(customer.CurrentContainerId, contents)
        { StateId = ++state, CarriedItem = customer.CarriedItem });
        await customer.QueuePacketAsync(new MerchantOffersPacket
        {
            WindowId = customer.CurrentContainerId,
            Offers = offers,
            VillagerLevel = level,
            VillagerExperience = experience,
            IsRegularVillager = canRestock,
            CanRestock = canRestock
        });
    }

    private Microsoft.Extensions.Logging.ILogger CustomerLogger => customer is IClientPlayer connected
        ? connected.Client.Logger
        : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    // The legacy merchant click lock precedes BaseContainer's ordered window locks (also in SendInTurnAsync).
    // Death retires the menu here, then reclaims its inputs with both sets of locks, before any notification.
    internal async ValueTask<IDisposable> LockDeathAsync()
    {
        await this.clicks.WaitAsync();
        this.closed = true;
        return new ClickRelease(this.clicks);
    }

    private sealed class ClickRelease(System.Threading.SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}
