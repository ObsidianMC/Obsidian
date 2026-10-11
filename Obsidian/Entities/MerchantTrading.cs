using Obsidian.API.Inventory;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Packets.Play.Serverbound;

namespace Obsidian.Entities;

internal static class MerchantTrading
{
    internal static async ValueTask OpenAsync(Mob merchant, IPlayer player, ImmutableArray<TradeEntry> offers,
        int level, int experience, bool canRestock, Action<TradeEntry>? completed = null)
    {
        if (!merchant.Alive || player.Health <= 0 || player.GameMode == GameMode.Spectator ||
            merchant.Level != player.Level || !merchant.IsInRange(player, 4) || offers.IsEmpty ||
            merchant.Level.GetPlayersInRange(merchant.Position, 16).Any(other => other.OpenedContainer is MerchantContainer menu && ReferenceEquals(menu.Merchant, merchant))) return;
        if (player.OpenedContainer is MerchantContainer previous) await previous.CloseAsync(player);
        var container = new MerchantContainer(merchant, player, offers, level, experience, canRestock, completed);
        await player.OpenInventoryAsync(container);
        await container.SyncAsync();
    }
}

internal sealed class MerchantContainer : BaseContainer
{
    internal Mob Merchant { get; }
    private readonly IPlayer customer;
    private readonly ImmutableArray<TradeEntry> offers;
    private readonly bool canRestock;
    private readonly Action<TradeEntry>? completed;
    private readonly System.Threading.SemaphoreSlim clicks = new(1, 1);
    private int level;
    private int experience;
    private int selected;
    private int state;
    private bool closed;

    internal MerchantContainer(Mob merchant, IPlayer player, ImmutableArray<TradeEntry> offers, int level,
        int experience, bool canRestock, Action<TradeEntry>? completed) : base(3, InventoryType.Merchant)
    {
        Merchant = merchant; customer = player; this.offers = offers; this.level = level; this.experience = experience;
        this.canRestock = canRestock; this.completed = completed;
        Title = merchant.CustomName ?? ChatMessage.Simple(merchant.Type == EntityType.Villager ? "Villager" : "Wandering Trader");
    }

    internal async ValueTask SelectAsync(int index)
    {
        await clicks.WaitAsync();
        try
        {
            if (closed || index < 0 || index >= offers.Length || !Merchant.Alive || Merchant.Level != customer.Level || !Merchant.IsInRange(customer, 8)) return;
            selected = index;
            ReturnInputs();
            var offer = offers[index];
            FillInput(0, offer.FirstInput);
            FillInput(1, offer.SecondInput);
            await SyncAsync();
        }
        finally { clicks.Release(); }
    }

    private static int Cost(TradeEntry offer) => Math.Clamp(offer.FirstInput.Count +
        Math.Max(0, (int)Math.Floor(offer.FirstInput.Count * offer.Multiplier * offer.Demand)) + offer.Discount, 1, 64);
    private static bool Matches(ItemStack? stack, TradeItem required) => required.Count <= 0 || stack != null &&
        stack.Holder.Id == required.ID && required.Components.All(component => stack.TryGetComponent(component.Type, out var actual) && Equals(actual, component));
    private bool CanTrade(TradeEntry offer) => !offer.IsDisabled && offer.UsedCount < offer.MaxCount &&
        Matches(items[0], offer.FirstInput) && items[0]?.Count >= Cost(offer) &&
        Matches(items[1], offer.SecondInput) && (offer.SecondInput.Count <= 0 || items[1]?.Count >= offer.SecondInput.Count);

    private void FillInput(int slot, TradeItem required)
    {
        if (required.Count <= 0) return;
        for (var index = 9; index <= 44; index++)
        {
            var stack = customer.Inventory.GetItem(index);
            if (!Matches(stack, required) || stack == null) continue;
            var amount = Math.Min(stack.Count, stack.MaxStackSize - (items[slot]?.Count ?? 0));
            if (amount == 0) break;
            if (items[slot] == null) items[slot] = new ItemStack(stack, amount);
            else if (items[slot] == stack) items[slot]!.Count += amount;
            else continue;
            customer.Inventory.RemoveItem(index, amount);
        }
    }

    internal async ValueTask HandleClickAsync(ContainerClickPacket packet, IPlayer player)
    {
        await clicks.WaitAsync();
        try
        {
            if (closed || player != customer || packet.ContainerId != player.CurrentContainerId) return;
            if (!Merchant.Alive || Merchant.Level != player.Level || !Merchant.IsInRange(player, 8)) { await CloseAsync(player); return; }
            if (packet.StateId != state) { await SyncAsync(); return; }
            var slot = packet.ClickedSlot;
            if (slot == 2 && (packet.ClickType == ClickType.QuickMove || packet.ClickType == ClickType.Pickup && packet.Button is 0 or 1))
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
                    stack.Count += amount; carried.Count -= amount;
                }
                else if (packet.Button == 0) { source.SetItem(mapped.Slot, carried); customer.CarriedItem = stack; }
                if (customer.CarriedItem is { Count: <= 0 }) customer.CarriedItem = null;
            }
            else if (slot is >= 0 and < 39 && packet.ClickType == ClickType.QuickMove)
            {
                if (slot < 2) { if (items[slot] != null) { StoreInInventory(items[slot]!); if (items[slot]!.Count <= 0) items[slot] = null; } }
                else
                {
                    var mapped = GetSlot(slot);
                    var stack = customer.Inventory.GetItem(mapped.Slot);
                    if (stack != null)
                        for (var input = 0; input < 2; input++)
                        {
                            var required = input == 0 ? offers[selected].FirstInput : offers[selected].SecondInput;
                            if (required.Count <= 0 || !Matches(stack, required) || items[input] != null && items[input] != stack) continue;
                            var amount = Math.Min(stack.Count, stack.MaxStackSize - (items[input]?.Count ?? 0));
                            if (items[input] == null) items[input] = new ItemStack(stack, amount); else items[input]!.Count += amount;
                            customer.Inventory.RemoveItem(mapped.Slot, amount);
                            break;
                        }
                }
            }
            await SyncAsync();
        }
        finally { clicks.Release(); }
    }

    private void TakeResult(bool quickMove)
    {
        var offer = offers[selected];
        while (CanTrade(offer))
        {
            var result = new ItemStack(offer.Output, offer.Output.Count);
            if (quickMove)
            {
                var capacity = Enumerable.Range(9, 36).Sum(index => customer.Inventory.GetItem(index) is { } stack ?
                    stack == result ? stack.MaxStackSize - stack.Count : 0 : result.MaxStackSize);
                if (capacity < result.Count) break;
                StoreInInventory(result);
            }
            else
            {
                var cursor = customer.CarriedItem;
                if (cursor != null && !cursor.IsAir && (cursor != result || cursor.Count + result.Count > cursor.MaxStackSize)) break;
                if (cursor == null || cursor.IsAir) customer.CarriedItem = result; else cursor.Count += result.Count;
            }
            RemoveItem(0, Cost(offer));
            if (offer.SecondInput.Count > 0) RemoveItem(1, offer.SecondInput.Count);
            offer.UsedCount++;
            offer.IsDisabled = offer.UsedCount >= offer.MaxCount;
            experience += offer.XP;
            completed?.Invoke(offer);
            if (canRestock) level = experience >= 250 ? 5 : experience >= 150 ? 4 : experience >= 70 ? 3 : experience >= 10 ? 2 : 1;
            if (Merchant.Level.LevelData.GetBooleanRule("mob_drops")) Merchant.Level.SpawnExperienceOrbs(Merchant.Position, 3);
            if (!quickMove) break;
        }
    }

    private void StoreInInventory(ItemStack stack)
    {
        for (var pass = 0; pass < 2 && stack.Count > 0; pass++)
        for (var index = 9; index <= 44 && stack.Count > 0; index++)
        {
            var target = customer.Inventory.GetItem(index);
            if (pass == 0 && target == stack && target != null)
            { var amount = Math.Min(stack.Count, target.MaxStackSize - target.Count); target.Count += amount; stack.Count -= amount; }
            else if (pass == 1 && (target == null || target.IsAir || target.Count <= 0))
            { var amount = Math.Min(stack.Count, stack.MaxStackSize); customer.Inventory.SetItem(index, new ItemStack(stack, amount)); stack.Count -= amount; }
        }
    }

    private void ReturnInputs()
    {
        for (var slot = 0; slot < 2; slot++)
        {
            if (items[slot] is not { } stack) continue;
            StoreInInventory(stack);
            if (stack.Count > 0) Merchant.Level.SpawnEntity(new ItemEntity
            { Level = Merchant.Level, EntityId = Server.GetNextEntityId(), Position = customer.Position, Item = stack });
            items[slot] = null;
        }
    }

    internal async ValueTask CloseAsync(IPlayer player)
    {
        if (closed) return;
        closed = true;
        ReturnInputs();
        player.OpenedContainer = null;
        await player.QueuePacketAsync(new Obsidian.Net.Packets.Play.Clientbound.ContainerClosePacket { ContainerId = player.CurrentContainerId });
        await player.QueuePacketAsync(new ContainerSetContentPacket(0, player.Inventory.ToList()) { CarriedItem = player.CarriedItem });
    }

    internal async ValueTask SyncAsync()
    {
        items[2] = CanTrade(offers[selected]) ? new ItemStack(offers[selected].Output, offers[selected].Output.Count) : null;
        var contents = items.Concat(Enumerable.Range(9, 36).Select(index => customer.Inventory.GetItem(index))).ToList();
        await customer.QueuePacketAsync(new ContainerSetContentPacket(customer.CurrentContainerId, contents)
        { StateId = ++state, CarriedItem = customer.CarriedItem });
        await customer.QueuePacketAsync(new MerchantOffersPacket { WindowId = customer.CurrentContainerId, Offers = offers,
            VillagerLevel = level, VillagerExperience = experience, IsRegularVillager = canRestock, CanRestock = canRestock });
    }
}
