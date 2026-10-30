using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Obsidian.API.Containers;
using Obsidian.API.Crafting;
using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Loot;
using Obsidian.Entities;
using Obsidian.Net;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.WindowProperties;
using Obsidian.WorldData;
using Obsidian.WorldData.Maps;

namespace Obsidian.Events;

// Vanilla's workstations other than the crafting and enchanting tables: the anvil, smithing table, stonecutter, loom,
// grindstone and cartography table. Each shows a result worked out from its inputs, which the player takes like a
// crafting result; taking it uses up the inputs.
public partial class MainEventHandler
{
    // The highest anvil cost a survival player can pay (vanilla's AnvilMenu.MAX_COST).
    private const int MaxAnvilCost = 40;

    // The longest name an anvil gives (vanilla's AnvilMenu.MAX_NAME_LENGTH).
    private const int MaxItemNameLength = 50;

    private const int MaxBannerLayers = 6;

    // What a click in a workstation does after the window's lock: experience, sounds and block changes.
    [ThreadStatic]
    private static List<Func<ValueTask>>? workstationEffects;

    // Which item each trim material comes from (its provides_trim_material).
    private static readonly Dictionary<int, string> trimMaterials = new()
    {
        [ItemIds.AmethystShard] = "minecraft:amethyst",
        [ItemIds.CopperIngot] = "minecraft:copper",
        [ItemIds.Diamond] = "minecraft:diamond",
        [ItemIds.Emerald] = "minecraft:emerald",
        [ItemIds.GoldIngot] = "minecraft:gold",
        [ItemIds.IronIngot] = "minecraft:iron",
        [ItemIds.LapisLazuli] = "minecraft:lapis",
        [ItemIds.NetheriteIngot] = "minecraft:netherite",
        [ItemIds.Quartz] = "minecraft:quartz",
        [ItemIds.Redstone] = "minecraft:redstone",
        [ItemIds.ResinBrick] = "minecraft:resin",
    };

    /// <summary>
    /// Whether the window is one of vanilla's workstations handled here.
    /// </summary>
    internal static bool IsWorkstation(BaseContainer? window) =>
        window is AnvilContainer or SmithingTable or Stonecutter or Loom or Grindstone or CartographyTable;

    /// <summary>
    /// Whether the window holds items only while it's open, which go back to the player as it closes: a
    /// workstation's inputs, a beacon's payment.
    /// </summary>
    internal static bool HoldsItemsWhileOpen(BaseContainer? window) => IsWorkstation(window) || window is BeaconContainer;

    private static int WorkstationResultSlot(BaseContainer window) => window switch
    {
        AnvilContainer => AnvilContainer.ResultSlot,
        SmithingTable => SmithingTable.ResultSlot,
        Stonecutter => Stonecutter.ResultSlot,
        Loom => Loom.ResultSlot,
        Grindstone => Grindstone.ResultSlot,
        CartographyTable => CartographyTable.ResultSlot,
        _ => -1
    };

    /// <summary>
    /// Handles a click in a workstation's window: its result slot gives the result, the others work like any window's.
    /// </summary>
    /// <returns>Whether the window is a workstation's, so that the click is handled.</returns>
    private static bool HandleWorkstation(ContainerClickEventArgs args)
    {
        var station = args.Player.OpenedContainer;
        if (station is null || !IsWorkstation(station))
            return false;

        if (args.Container == station
            && args.ClickedSlot == WorkstationResultSlot(station)
            && args.ClickType != ClickType.QuickCraft)
        {
            TakeWorkstationResult(args, station);
        }
        else
        {
            HandleClick(args, dropThrown: true);
        }

        return true;
    }

    /// <summary>
    /// Starts collecting what clicks in workstations do after the window's lock.
    /// </summary>
    private static void BeginWorkstationEffects() => workstationEffects = [];

    /// <summary>
    /// Stops collecting, with what the click since <see cref="BeginWorkstationEffects"/> left for after the lock. Call
    /// it on the same thread, with no await in between.
    /// </summary>
    private static List<Func<ValueTask>> EndWorkstationEffects()
    {
        var effects = workstationEffects ?? [];
        workstationEffects = null;

        return effects;
    }

    private static async ValueTask RunWorkstationEffectsAsync(List<Func<ValueTask>> effects)
    {
        foreach (var effect in effects)
            await effect();
    }

    private static void AfterClick(Func<ValueTask> effect)
    {
        if (workstationEffects is { } effects)
            effects.Add(effect);
        else
            _ = effect().AsTask();
    }

    /// <summary>
    /// Takes a workstation's result like vanilla's result slots: a pickup moves it to the cursor (or onto a cursor of
    /// the same item, if it fits whole), a shift-click to the player's inventory, filling from the hotbar's last slot,
    /// a swap to an empty hotbar slot or the off hand, a throw drops it. Each take uses up the inputs; a shift-click
    /// takes again while the result stays the same item.
    /// </summary>
    private static void TakeWorkstationResult(ContainerClickEventArgs args, BaseContainer station)
    {
        var player = args.Player;
        if (args.ClickType == ClickType.Clone)
        {
            HandleClone(args);
            return;
        }

        var slot = WorkstationResultSlot(station);
        var repeats = args.ClickType == ClickType.QuickMove;
        var taken = station.GetItem(slot)?.Holder;

        while (station.GetItem(slot) is ItemStack result
            && !result.IsAir
            && result.Holder == taken
            && MayTakeWorkstationResult(player, station))
        {
            var stack = Copy(result)!;

            // A cartography table's map becomes its new map as it's taken.
            if (station is CartographyTable && !CanPlaceWorkstationResult(args))
                return;

            if (station is CartographyTable)
                FinishCartographyMap(player, stack);

            switch (args.ClickType)
            {
                case ClickType.Pickup when args.Button is 0 or 1:
                    var carried = player.CarriedItem;
                    if (carried.IsNullOrAir())
                        player.CarriedItem = stack;
                    else if (carried == stack && carried.Count + stack.Count <= carried.MaxStackSize)
                        carried.Count += stack.Count;
                    else
                        return;

                    break;
                case ClickType.QuickMove:
                    if (!MoveItemStackTo(player, stack, player.Inventory, 9, 45, reverse: true))
                        return;

                    break;
                case ClickType.Swap when args.Button is >= 0 and <= 8 or 40:
                    var hotbar = args.Button == 40 ? OffhandSlot : args.Button + 36;
                    if (!player.Inventory.GetItem(hotbar).IsNullOrAir())
                        return;

                    player.Inventory.SetItem(hotbar, stack);
                    break;
                case ClickType.Throw when args.Button is 0 or 1 && player.CarriedItem.IsNullOrAir():
                    SpawnThrownItem(player, stack);
                    break;
                default:
                    return;
            }

            station.SetItem(slot, null);
            TakeWorkstationInputs(player, station);

            // Like vanilla's shift-click, what didn't fit drops.
            SpawnThrownItem(player, stack.Count > 0 && args.ClickType == ClickType.QuickMove ? stack : null);
            RefreshWorkstationResult(player, station);

            if (!repeats)
                return;
        }
    }

    // Whether the take will place the result, so that a cartography table makes a new map only for a map taken.
    private static bool CanPlaceWorkstationResult(ContainerClickEventArgs args)
    {
        var player = args.Player;
        return args.ClickType switch
        {
            ClickType.Pickup when args.Button is 0 or 1 => player.CarriedItem.IsNullOrAir(),
            ClickType.QuickMove => Enumerable.Range(9, 36).Any(slot => player.Inventory.GetItem(slot).IsNullOrAir()),
            ClickType.Swap when args.Button is >= 0 and <= 8 or 40 =>
                player.Inventory.GetItem(args.Button == 40 ? OffhandSlot : args.Button + 36).IsNullOrAir(),
            ClickType.Throw when args.Button is 0 or 1 => player.CarriedItem.IsNullOrAir(),
            _ => false
        };
    }

    // Vanilla's result slots' mayPickup: an anvil's needs a cost the player can pay.
    private static bool MayTakeWorkstationResult(IPlayer player, BaseContainer station) => station is not AnvilContainer anvil
        || anvil.Cost > 0 && (player.GameMode == GameMode.Creative || player.XpLevel >= anvil.Cost);

    /// <summary>
    /// Uses up a workstation's inputs as its result is taken, like each vanilla menu's <c>onTake</c>.
    /// </summary>
    private static void TakeWorkstationInputs(IPlayer player, BaseContainer station)
    {
        var creative = player.GameMode == GameMode.Creative;

        switch (station)
        {
            case AnvilContainer anvil:
                if (!creative)
                {
                    player.XpLevel -= anvil.Cost;
                    AfterClick(() => player.QueuePacketAsync(new SetExperiencePacket(player.XpP, player.XpLevel, player.XpTotal)));
                }

                anvil.SetItem(0, null);
                if (anvil.RepairItemCountCost > 0 && anvil.GetItem(1) is { } addition && addition.Count > anvil.RepairItemCountCost)
                    anvil.SetItem(1, new ItemStack(addition, addition.Count - anvil.RepairItemCountCost));
                else
                    anvil.SetItem(1, null);

                anvil.Cost = 0;
                anvil.RepairItemCountCost = 0;
                anvil.ItemName = null;
                DamageAnvil(player, anvil, creative);
                break;
            case SmithingTable table:
                Shrink(table, SmithingTable.TemplateSlot);
                Shrink(table, SmithingTable.BaseSlot);
                Shrink(table, SmithingTable.AdditionSlot);
                LevelEvent(player, table.BlockPosition, 1044);
                break;
            case Stonecutter cutter:
                Shrink(cutter, Stonecutter.InputSlot);
                if (cutter.GetItem(Stonecutter.InputSlot).IsNullOrAir())
                    cutter.SelectedRecipe = -1;

                TakeSound(player, cutter, cutter.BlockPosition, "minecraft:ui.stonecutter.take_result");
                break;
            case Loom loom:
                Shrink(loom, Loom.BannerSlot);
                Shrink(loom, Loom.DyeSlot);
                if (loom.GetItem(Loom.BannerSlot).IsNullOrAir() || loom.GetItem(Loom.DyeSlot).IsNullOrAir())
                    loom.SelectedPattern = -1;

                TakeSound(player, loom, loom.BlockPosition, "minecraft:ui.loom.take_result");
                break;
            case Grindstone grindstone:
                var experience = GrindstoneExperience(grindstone);
                grindstone.SetItem(0, null);
                grindstone.SetItem(1, null);
                if (experience > 0 && player.Level is AbstractLevel level)
                {
                    var center = (VectorD)grindstone.BlockPosition + new VectorD(0.5, 0.5, 0.5);
                    AfterClick(() =>
                    {
                        level.DropCookingExperience(center, experience);
                        return ValueTask.CompletedTask;
                    });
                }

                LevelEvent(player, grindstone.BlockPosition, 1042);
                break;
            case CartographyTable table:
                Shrink(table, CartographyTable.MapSlot);
                Shrink(table, CartographyTable.AdditionalSlot);
                TakeSound(player, table, table.BlockPosition, "minecraft:ui.cartography_table.take_result");
                break;
        }

        static void Shrink(BaseContainer container, int slot)
        {
            if (container.GetItem(slot) is { Count: > 1 } item)
                container.SetItem(slot, new ItemStack(item, item.Count - 1));
            else
                container.SetItem(slot, null);
        }
    }

    /// <summary>
    /// Like vanilla's <c>AnvilMenu.onTake</c>: out of creative mode, an anvil wears a step (chipped, then damaged, then
    /// gone) with a chance of 12% each use.
    /// </summary>
    private static void DamageAnvil(IPlayer player, AnvilContainer anvil, bool creative)
    {
        if (player.Level is not AbstractLevel level)
            return;

        var position = anvil.BlockPosition;
        var wears = !creative && Globals.Random.NextSingle() < 0.12f;

        AfterClick(async () =>
        {
            var block = await level.GetBlockAsync(position);
            if (block is null || !TagsRegistry.Block.Anvil.Entries.Contains(block.RegistryId))
                return;

            if (!wears)
            {
                level.PacketBroadcaster.QueuePacketToLevelInRange(level, (VectorD)position, new LevelEventPacket(1030, position, 0));
                return;
            }

            IBlock? next = block.RegistryId switch
            {
                BlockIds.Anvil => BlocksRegistry.ChippedAnvil,
                BlockIds.ChippedAnvil => BlocksRegistry.DamagedAnvil,
                _ => null
            };

            if (next is null)
            {
                await level.SetBlockAsync(position, BlocksRegistry.Air, true);
                level.PacketBroadcaster.QueuePacketToLevelInRange(level, (VectorD)position, new LevelEventPacket(1029, position, 0));
                await player.CloseContainerAsync(null);
                return;
            }

            if (block.GetProperty("facing") is string facing)
                next = next.WithProperty("facing", facing);

            await level.SetBlockAsync(position, next, true);
            level.PacketBroadcaster.QueuePacketToLevelInRange(level, (VectorD)position, new LevelEventPacket(1030, position, 0));
        });
    }

    private static void LevelEvent(IPlayer player, Vector position, int type)
    {
        if (player.Level is not AbstractLevel level)
            return;

        AfterClick(() =>
        {
            level.PacketBroadcaster.QueuePacketToLevelInRange(level, (VectorD)position, new LevelEventPacket(type, position, 0));
            return ValueTask.CompletedTask;
        });
    }

    /// <summary>
    /// Vanilla's result-take sound: <c>Level.playSound(null, pos, …)</c> to everyone near the workstation, at most once
    /// per game tick per station (the menu's <c>lastSoundTime</c>).
    /// </summary>
    private static void TakeSound(IPlayer player, BaseContainer station, Vector position, string sound)
    {
        AfterClick(() =>
        {
            if (player.Level is not AbstractLevel level)
                return ValueTask.CompletedTask;

            var clock = TakeSoundClocks.GetValue(station, _ => new TakeSoundClock());
            var time = level.LevelData.GameTime;
            if (clock.LastTime == time)
                return ValueTask.CompletedTask;

            clock.LastTime = time;
            level.BlockUpdates.PlaySound(position, sound);
            return ValueTask.CompletedTask;
        });
    }

    private sealed class TakeSoundClock
    {
        public long LastTime { get; set; } = long.MinValue;
    }

    private static readonly ConditionalWeakTable<BaseContainer, TakeSoundClock> TakeSoundClocks = new();

    /// <summary>
    /// Sets the result slot of the player's open workstation, if they have one open, to what its inputs give.
    /// </summary>
    private static void RefreshWorkstationResult(IPlayer player)
    {
        if (player.OpenedContainer is BaseContainer station && IsWorkstation(station))
            RefreshWorkstationResult(player, station);
    }

    private static void RefreshWorkstationResult(IPlayer player, BaseContainer station)
    {
        var result = station switch
        {
            AnvilContainer anvil => AnvilResult(player, anvil),
            SmithingTable table => SmithingResult(player, table),
            Stonecutter cutter => StonecutterResult(player, cutter),
            Loom loom => LoomResult(loom),
            Grindstone grindstone => GrindstoneResult(grindstone),
            CartographyTable table => CartographyResult(player, table),
            _ => null
        };

        var slot = WorkstationResultSlot(station);
        var current = station.GetItem(slot);
        var same = result.IsNullOrAir()
            ? current.IsNullOrAir()
            : current is not null && current == result && current.Count == result.Count;

        // Setting the same result again would count as a change (see BaseContainer.Revision).
        if (!same)
            station.SetItem(slot, result.IsNullOrAir() ? null : result);
    }

    /// <summary>
    /// The window properties the player's open workstation shows: an anvil's cost, a stonecutter's chosen recipe and a
    /// loom's chosen pattern.
    /// </summary>
    internal static IEnumerable<IClientboundPacket> WorkstationProperties(IPlayer player)
    {
        IWindowProperty? property = player.OpenedContainer switch
        {
            AnvilContainer anvil => new AnvilWindowProperty((short)anvil.Cost),
            Stonecutter cutter => new StonecutterWindowProperty((short)cutter.SelectedRecipe),
            Loom loom => new LoomWindowProperty((short)loom.SelectedPattern),
            _ => null
        };

        if (property is not null)
            yield return new ContainerSetDataPacket { ContainerId = player.CurrentContainerId, WindowProperty = property };

        foreach (var data in RedstoneMenuProperties(player))
            yield return new ContainerSetDataPacket { ContainerId = player.CurrentContainerId, WindowProperty = data };
    }

    #region Anvil

    /// <summary>
    /// What an anvil makes, like vanilla's <c>AnvilMenu.createResult</c>: the item repaired with its material or a
    /// second one of it, with the second's (or a book's) enchantments, and renamed; with the levels it costs.
    /// </summary>
    private static ItemStack? AnvilResult(IPlayer player, AnvilContainer anvil)
    {
        anvil.Cost = 0;
        anvil.RepairItemCountCost = 0;

        var input = anvil.GetItem(0);
        if (input.IsNullOrAir())
            return null;

        var creative = player.GameMode == GameMode.Creative;
        var result = Copy(input)!;
        var addition = anvil.GetItem(1);
        var enchantments = EnchantmentHelper.GetEnchantments(input).ToDictionary(enchantment => enchantment.Id, enchantment => enchantment.Level);
        var extraCost = 0;
        long repairCost = RepairCostOf(input) + RepairCostOf(addition);
        var renameCost = 0;

        if (!addition.IsNullOrAir())
        {
            var isBook = addition.ContainsKey(DataComponentType.StoredEnchantments) || addition.Is(ItemsRegistry.EnchantedBook);

            if (IsDamageable(result) && IsValidRepairItem(input, addition))
            {
                var repair = Math.Min(result.Damage, result.MaxDamage / 4);
                if (repair <= 0)
                    return null;

                var used = 0;
                for (; repair > 0 && used < addition.Count; used++)
                {
                    SetDamage(result, result.Damage - repair);
                    extraCost++;
                    repair = Math.Min(result.Damage, result.MaxDamage / 4);
                }

                anvil.RepairItemCountCost = used;
            }
            else
            {
                if (!isBook && (result.Holder != addition.Holder || !IsDamageable(result)))
                    return null;

                if (IsDamageable(result) && !isBook)
                {
                    var durability = input.MaxDamage - input.Damage;
                    var added = addition.MaxDamage - addition.Damage + result.MaxDamage * 12 / 100;
                    var damage = Math.Max(0, result.MaxDamage - (durability + added));
                    if (damage < result.Damage)
                    {
                        SetDamage(result, damage);
                        extraCost += 2;
                    }
                }

                var anyCompatible = false;
                var anyIncompatible = false;

                foreach (var added in EnchantmentHelper.GetEnchantments(addition))
                {
                    if (EnchantmentById(added.Id) is not EnchantmentDefinition enchantment)
                        continue;

                    var current = enchantments.GetValueOrDefault(added.Id);
                    var level = current == added.Level ? added.Level + 1 : Math.Max(added.Level, current);
                    var applies = enchantment.CanEnchant(input) || creative || input.Is(ItemsRegistry.EnchantedBook);

                    foreach (var other in enchantments.Keys)
                    {
                        if (other != added.Id && EnchantmentById(other) is { } existing
                            && !EnchantmentDefinition.AreCompatible(enchantment, existing))
                        {
                            applies = false;
                            extraCost++;
                        }
                    }

                    if (!applies)
                    {
                        anyIncompatible = true;
                        continue;
                    }

                    anyCompatible = true;
                    level = Math.Min(level, enchantment.MaxLevel);
                    enchantments[added.Id] = level;

                    var cost = enchantment.AnvilCost;
                    if (isBook)
                        cost = Math.Max(1, cost / 2);

                    extraCost += cost * level;
                    if (input.Count > 1)
                        extraCost = MaxAnvilCost;
                }

                if (anyIncompatible && !anyCompatible)
                    return null;
            }
        }

        if (!string.IsNullOrWhiteSpace(anvil.ItemName))
        {
            if (anvil.ItemName != PlainText(input.CustomName))
            {
                renameCost = 1;
                extraCost += renameCost;
                result[DataComponentType.CustomName] = ComponentBuilder.CustomName with { Value = new ChatMessage { Text = anvil.ItemName } };
            }
        }
        else if (input.CustomName is not null)
        {
            renameCost = 1;
            extraCost += renameCost;
            RemoveComponent(result, DataComponentType.CustomName);
        }

        anvil.Cost = extraCost <= 0 ? 0 : (int)Math.Clamp(repairCost + extraCost, 0, int.MaxValue);
        if (extraCost <= 0)
            return null;

        if (renameCost == extraCost && renameCost > 0 && anvil.Cost >= MaxAnvilCost)
            anvil.Cost = MaxAnvilCost - 1;

        if (anvil.Cost >= MaxAnvilCost && !creative)
            return null;

        var resultRepairCost = Math.Max(RepairCostOf(result), RepairCostOf(addition));
        if (renameCost != extraCost || renameCost == 0)
            resultRepairCost = IncreasedRepairCost(resultRepairCost);

        result[DataComponentType.RepairCost] = ComponentBuilder.RepairCost with { Value = resultRepairCost };
        EnchantmentHelper.SetEnchantments(result, [.. enchantments.Select(pair => new Enchantment { Id = pair.Key, Level = pair.Value })]);

        return result;
    }

    /// <summary>
    /// Sets the name an anvil gives its result, like vanilla's <c>AnvilMenu.setItemName</c>: a name longer than 50
    /// characters is refused.
    /// </summary>
    internal static async ValueTask RenameAnvilItemAsync(IPlayer player, string name)
    {
        if (player.OpenedContainer is not AnvilContainer anvil)
            return;

        var filtered = new string([.. name.Where(character => character >= ' ' && character != '\u007f' && character != '§')]);
        if (filtered.Length > MaxItemNameLength)
            return;

        using (await BaseContainer.LockWindowAsync(anvil, player.Inventory))
        {
            if (!ReferenceEquals(player.OpenedContainer, anvil))
                return;

            anvil.ItemName = filtered;
            RefreshWorkstationResult(player, anvil);
        }

        await RefreshViewersAsync(anvil);
    }

    // Vanilla's AnvilMenu.calculateIncreasedRepairCost.
    private static int IncreasedRepairCost(int cost) => (int)Math.Min(cost * 2L + 1, int.MaxValue);

    private static int RepairCostOf(ItemStack? item) =>
        item.IsNullOrAir() ? 0 : item.GetComponent<SimpleDataComponent<int>>(DataComponentType.RepairCost)?.Value ?? 0;

    private static bool IsDamageable(ItemStack item) =>
        item.MaxDamage > 0 && !item.Unbreakable;

    private static void SetDamage(ItemStack item, int damage) =>
        item[DataComponentType.Damage] = ComponentBuilder.Damage with { Value = Math.Clamp(damage, 0, item.MaxDamage) };

    // Vanilla's isValidRepairItem: the item's repairable component lists the material.
    private static bool IsValidRepairItem(ItemStack item, ItemStack material)
    {
        if (item.GetComponent<SimpleDataComponent<IdSet>>(DataComponentType.Repairable)?.Value is not IdSet repairable)
            return false;

        return repairable.Type == 0
            ? TagsRegistry.Contains("item", repairable.TagName!, material.Holder.Id)
            : repairable.Ids?.Contains(material.Holder.Id) == true;
    }

    private static EnchantmentDefinition? EnchantmentById(int id) =>
        EnchantmentsRegistry.All.FirstOrDefault(enchantment => enchantment.Id == id);

    // A name's text without its formatting, like vanilla's Component.getString.
    private static string? PlainText(ChatMessage? message) => message is null
        ? null
        : (message.Text ?? "") + string.Concat(message.GetExtras().Select(PlainText));

    // Removes a component a stack has, as patch of its own or as its item's default.
    private static void RemoveComponent(ItemStack item, DataComponentType type)
    {
        item.Remove(type);
        if (!item.Holder.DefaultComponents.Any(component => component.Type == type))
            item.RemoveComponents.Remove(type);
    }

    #endregion

    #region Smithing table

    /// <summary>
    /// What a smithing table makes, like vanilla's <c>SmithingMenu.createResult</c>: the first smithing recipe its
    /// template, base and addition match, either turning the base into another item or trimming it.
    /// </summary>
    private static ItemStack? SmithingResult(IPlayer player, SmithingTable table)
    {
        var template = table.GetItem(SmithingTable.TemplateSlot);
        var baseItem = table.GetItem(SmithingTable.BaseSlot);
        var addition = table.GetItem(SmithingTable.AdditionSlot);

        foreach (var recipe in PlayerRecipes(player).Recipes.Values)
        {
            switch (recipe)
            {
                case SmithingTransformRecipe transform
                    when Matches(transform.Template, template) && Matches(transform.Base, baseItem) && Matches(transform.Addition, addition):
                    var output = transform.Result.First();
                    var upgraded = baseItem!.TransmuteCopy(output.Holder);
                    upgraded.Count = Math.Max(1, output.Count);
                    foreach (var component in output.Patch)
                        upgraded[component.Type] = component;

                    return upgraded;
                case SmithingTrimRecipe trim
                    when Matches(trim.Template, template) && Matches(trim.Base, baseItem) && Matches(trim.Addition, addition):
                    return TrimmedItem(trim, baseItem!, addition!);
            }
        }

        return null;

        static bool Matches(Ingredient ingredient, ItemStack? item) => item.IsNullOrAir()
            ? ingredient.Count == 0
            : ingredient.Any(choice => choice.Holder == item.Holder);
    }

    // Vanilla's SmithingTrimRecipe.applyTrim: the base with the addition's material in the recipe's pattern, unless
    // it has that trim already.
    private static ItemStack? TrimmedItem(SmithingTrimRecipe recipe, ItemStack baseItem, ItemStack addition)
    {
        if (recipe.Pattern is not string patternName
            || !trimMaterials.TryGetValue(addition.Holder.Id, out var materialName)
            || !CodecRegistry.TrimMaterial.All.TryGetValue(materialName, out var material)
            || !CodecRegistry.TrimPattern.All.TryGetValue(patternName, out var pattern))
        {
            return null;
        }

        if (baseItem.GetComponent<TrimDataComponent>(DataComponentType.Trim) is { } existing
            && existing.MaterialRegistryId == material.Id
            && existing.PatternRegistryId == pattern.Id)
        {
            return null;
        }

        var trimmed = new ItemStack(baseItem, 1);
        trimmed[DataComponentType.Trim] = new TrimDataComponent
        {
            MaterialRegistryId = material.Id,
            PatternRegistryId = pattern.Id,
            Material = material.Element,
            Pattern = pattern.Element,
            ShowInToolTip = true
        };

        return trimmed;
    }

    // Whether an item goes in a smithing table's slot: what some smithing recipe takes there.
    private static bool SmithingAccepts(IPlayer player, int slot, ItemStack item)
    {
        return PlayerRecipes(player).Recipes.Values.Any(recipe => (recipe, slot) switch
        {
            (SmithingTransformRecipe transform, SmithingTable.TemplateSlot) => Takes(transform.Template),
            (SmithingTransformRecipe transform, SmithingTable.BaseSlot) => Takes(transform.Base),
            (SmithingTransformRecipe transform, SmithingTable.AdditionSlot) => Takes(transform.Addition),
            (SmithingTrimRecipe trim, SmithingTable.TemplateSlot) => Takes(trim.Template),
            (SmithingTrimRecipe trim, SmithingTable.BaseSlot) => Takes(trim.Base),
            (SmithingTrimRecipe trim, SmithingTable.AdditionSlot) => Takes(trim.Addition),
            _ => false
        });

        bool Takes(Ingredient ingredient) => ingredient.Any(choice => choice.Holder == item.Holder);
    }

    #endregion

    #region Stonecutter

    /// <summary>
    /// The stonecutter recipes that take <paramref name="input"/>, in the order its client lists them: by recipe
    /// identifier (see <see cref="UpdateRecipesPacket"/>).
    /// </summary>
    private static List<CuttingRecipe> StonecutterRecipes(IPlayer player, ItemStack? input)
    {
        if (input.IsNullOrAir())
            return [];

        return [.. PlayerRecipes(player).Recipes
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value)
            .OfType<CuttingRecipe>()
            .Where(recipe => recipe.Ingredient.Any(choice => choice.Holder == input.Holder))];
    }

    /// <summary>
    /// What a stonecutter makes, like vanilla's <c>StonecutterMenu</c>: the chosen recipe's result for its input. A
    /// different input clears the choice.
    /// </summary>
    private static ItemStack? StonecutterResult(IPlayer player, Stonecutter cutter)
    {
        var input = cutter.GetItem(Stonecutter.InputSlot);
        var holder = input.IsNullOrAir() ? null : input.Holder;
        if (holder != cutter.LastInput)
        {
            cutter.LastInput = holder;
            cutter.SelectedRecipe = -1;
        }

        var recipes = StonecutterRecipes(player, input);
        if (cutter.SelectedRecipe < 0 || cutter.SelectedRecipe >= recipes.Count)
            return null;

        var output = recipes[cutter.SelectedRecipe].Result.First();
        return Copy(output);
    }

    #endregion

    #region Loom

    /// <summary>
    /// What a loom makes, like vanilla's <c>LoomMenu.slotsChanged</c>: the banner with a layer of the chosen pattern in
    /// the dye's color, choosing it by itself when there's just one; nothing for a banner with six layers.
    /// </summary>
    private static ItemStack? LoomResult(Loom loom)
    {
        var banner = loom.GetItem(Loom.BannerSlot);
        var dye = loom.GetItem(Loom.DyeSlot);
        var patternItem = loom.GetItem(Loom.PatternSlot);

        var previous = loom.SelectablePatterns;
        loom.SelectablePatterns = banner.IsNullOrAir() || dye.IsNullOrAir()
            ? []
            : BannerPatternsRegistry.Selectable(patternItem.IsNullOrAir() ? null : patternItem.Holder.Id);

        if (loom.SelectablePatterns.IsEmpty)
        {
            loom.SelectedPattern = -1;
            return null;
        }

        int? chosen;
        if (loom.SelectablePatterns.Length == 1)
        {
            loom.SelectedPattern = 0;
            chosen = loom.SelectablePatterns[0];
        }
        else if (loom.SelectedPattern >= 0 && loom.SelectedPattern < previous.Length
            && loom.SelectablePatterns.IndexOf(previous[loom.SelectedPattern]) is var index and >= 0)
        {
            loom.SelectedPattern = index;
            chosen = loom.SelectablePatterns[index];
        }
        else
        {
            loom.SelectedPattern = -1;
            chosen = null;
        }

        var layers = banner!.GetComponent<BannerPatternsDataComponent>(DataComponentType.BannerPatterns);
        if (chosen is null || (layers?.Value.Length ?? 0) >= MaxBannerLayers || DyeOf(dye!) is not Dye color)
        {
            if ((layers?.Value.Length ?? 0) >= MaxBannerLayers)
                loom.SelectedPattern = -1;

            return null;
        }

        return WithBannerLayer(banner, chosen.Value, color);
    }

    // The banner, one of it, with a layer of the pattern on top.
    private static ItemStack WithBannerLayer(ItemStack banner, int pattern, Dye color)
    {
        var result = new ItemStack(banner, 1);
        var existing = result.GetComponent<BannerPatternsDataComponent>(DataComponentType.BannerPatterns);
        var layers = existing?.Value ?? [];
        var ids = existing?.RegistryIds ?? [];

        // A layer without a registry id keeps its inline data.
        while (ids.Length < layers.Length)
            ids = ids.Add(null);

        var identifier = BannerPatternsRegistry.Identifiers[pattern];
        var path = identifier["minecraft:".Length..];
        result[DataComponentType.BannerPatterns] = ComponentBuilder.BannerPatterns with
        {
            Value = layers.Add(new BannerPatternLayer
            {
                AssetId = identifier,
                TranslationKey = "block.minecraft.banner." + path,
                Color = color
            }),
            RegistryIds = ids.Add(pattern)
        };

        return result;
    }

    // The color of a dye item, like vanilla's DyeItem.
    private static Dye? DyeOf(ItemStack item)
    {
        var name = item.Holder.UnlocalizedName;
        var path = name.StartsWith("minecraft:", StringComparison.Ordinal) ? name["minecraft:".Length..] : name;
        if (!path.EndsWith("_dye", StringComparison.Ordinal))
            return null;

        return Enum.TryParse<Dye>(path[..^4].Replace("_", ""), ignoreCase: true, out var dye) ? dye : null;
    }

    #endregion

    #region Grindstone

    /// <summary>
    /// What a grindstone makes, like vanilla's <c>GrindstoneMenu.computeResult</c>: one item without its enchantments
    /// but curses, or two of the same item merged into one more durable, with the first's enchantments and the
    /// second's added, then all but curses removed.
    /// </summary>
    private static ItemStack? GrindstoneResult(Grindstone grindstone)
    {
        var input = grindstone.GetItem(0);
        var additional = grindstone.GetItem(1);
        var hasInput = !input.IsNullOrAir();
        var hasAdditional = !additional.IsNullOrAir();

        if (!hasInput && !hasAdditional)
            return null;

        if (hasInput && input!.Count > 1 || hasAdditional && additional!.Count > 1)
            return null;

        if (!hasInput || !hasAdditional)
        {
            var item = hasInput ? input! : additional!;
            return EnchantmentHelper.GetEnchantments(item).Length == 0 ? null : WithoutNonCurses(Copy(item)!);
        }

        if (input!.Holder != additional!.Holder)
            return null;

        var maxDamage = Math.Max(input.MaxDamage, additional.MaxDamage);
        var durability = input.MaxDamage - input.Damage + additional.MaxDamage - additional.Damage + maxDamage * 5 / 100;
        var count = 1;
        if (!IsDamageable(input))
        {
            if (input.MaxStackSize < 2 || input != additional)
                return null;

            count = 2;
        }

        var merged = new ItemStack(input, count);
        if (IsDamageable(merged))
        {
            merged[DataComponentType.MaxDamage] = ComponentBuilder.MaxDamage with { Value = maxDamage };
            SetDamage(merged, Math.Max(maxDamage - durability, 0));
        }

        var enchantments = EnchantmentHelper.GetEnchantments(merged).ToDictionary(enchantment => enchantment.Id, enchantment => enchantment.Level);
        foreach (var enchantment in EnchantmentHelper.GetEnchantments(additional))
        {
            if (!IsCurse(enchantment.Id) || !enchantments.ContainsKey(enchantment.Id))
                enchantments[enchantment.Id] = Math.Max(enchantments.GetValueOrDefault(enchantment.Id), enchantment.Level);
        }

        EnchantmentHelper.SetEnchantments(merged, [.. enchantments.Select(pair => new Enchantment { Id = pair.Key, Level = pair.Value })]);

        return WithoutNonCurses(merged);
    }

    // Vanilla's GrindstoneMenu.removeNonCursesFrom: an enchanted book left with none becomes a book, and the repair
    // cost restarts from the curses kept.
    private static ItemStack WithoutNonCurses(ItemStack item)
    {
        var curses = EnchantmentHelper.GetEnchantments(item).Where(enchantment => IsCurse(enchantment.Id)).ToArray();
        EnchantmentHelper.SetEnchantments(item, curses);

        if (item.Is(ItemsRegistry.EnchantedBook) && curses.Length == 0)
        {
            item = item.TransmuteCopy(ItemsRegistry.Book);
            RemoveComponent(item, DataComponentType.StoredEnchantments);
        }

        var repairCost = 0;
        for (var curse = 0; curse < curses.Length; curse++)
            repairCost = IncreasedRepairCost(repairCost);

        item[DataComponentType.RepairCost] = ComponentBuilder.RepairCost with { Value = repairCost };

        return item;
    }

    // Vanilla's GrindstoneMenu.getExperienceAmount: half to all of the enchanting cost of what's removed.
    private static int GrindstoneExperience(Grindstone grindstone)
    {
        var total = 0;
        for (var slot = 0; slot < 2; slot++)
        {
            if (grindstone.GetItem(slot) is not { Count: > 0 } item)
                continue;

            foreach (var enchantment in EnchantmentHelper.GetEnchantments(item))
            {
                if (!IsCurse(enchantment.Id) && EnchantmentById(enchantment.Id) is { } definition)
                    total += definition.MinCost.Calculate(enchantment.Level);
            }
        }

        if (total <= 0)
            return 0;

        var half = (int)Math.Ceiling(total / 2.0);
        return half + Globals.Random.Next(half);
    }

    private static bool IsCurse(int enchantment) => TagsRegistry.Enchantment.Curse.Entries.Contains(enchantment);

    // Vanilla's grindstone input slots: damageable or enchanted items.
    private static bool GrindstoneAccepts(ItemStack item) =>
        IsDamageable(item) || EnchantmentHelper.GetEnchantments(item).Length > 0;

    #endregion

    #region Cartography table

    /// <summary>
    /// What a cartography table makes, like vanilla's <c>CartographyTableMenu.setupResultSlot</c>: paper zooms an
    /// unlocked map out a scale (up to 4), a glass pane locks it, an empty map copies it.
    /// </summary>
    private static ItemStack? CartographyResult(IPlayer player, CartographyTable table)
    {
        var map = table.GetItem(CartographyTable.MapSlot);
        var additional = table.GetItem(CartographyTable.AdditionalSlot);
        if (map.IsNullOrAir() || additional.IsNullOrAir())
            return null;

        if (MapStorage.GetMapId(map) is not int id || MapStorage.For(player.Level).Get(id) is not MapData data)
            return null;

        // Vanilla 26.3 accepts map ids in the input slot, but only extendable_maps can be scaled with paper.
        if (additional.Is(ItemsRegistry.Paper)
            && TagsRegistry.Item.ExtendableMaps.Entries.Contains(map.Holder.Id)
            && !data.Locked
            && data.Scale < 4)
        {
            return WithPostProcessing(map, MapPostProcessingType.Scale);
        }

        if (additional.Is(ItemsRegistry.GlassPane) && !data.Locked)
            return WithPostProcessing(map, MapPostProcessingType.Lock);

        if (additional.Is(ItemsRegistry.Map))
            return new ItemStack(map, 2);

        return null;

        static ItemStack WithPostProcessing(ItemStack map, MapPostProcessingType type)
        {
            var result = new ItemStack(map, 1);
            result[DataComponentType.MapPostProcessing] = ComponentBuilder.MapPostProcessing with { Value = type };
            return result;
        }
    }

    /// <summary>
    /// Makes a map taken from a cartography table its new map, like vanilla's <c>MapItem.onCraftedPostProcess</c>: a
    /// zoomed-out map gets blank data at the next scale, a locked one a copy that no longer changes.
    /// </summary>
    internal static void FinishCartographyMap(IPlayer player, ItemStack map)
    {
        if (map.GetComponent<SimpleDataComponent<MapPostProcessingType>>(DataComponentType.MapPostProcessing) is not { } processing
            || MapStorage.GetMapId(map) is not int id)
        {
            return;
        }

        var storage = MapStorage.For(player.Level);
        RemoveComponent(map, DataComponentType.MapPostProcessing);
        if (storage.Get(id) is not MapData data)
            return;

        var newId = storage.GetFreeMapId();
        storage.Set(newId, processing.Value == MapPostProcessingType.Lock ? data.LockedCopy() : data.Scaled());
        map[DataComponentType.MapId] = ComponentBuilder.MapId with { Value = newId };
    }

    #endregion

    /// <summary>
    /// Whether <paramref name="item"/> goes in <paramref name="slot"/> of a workstation, like vanilla's menus' slots:
    /// never in the result; a smithing table's what its recipes take; a loom's a banner, a dye and a pattern item; a
    /// grindstone's damageable or enchanted items; a cartography table's a filled map, then paper, a map or a glass
    /// pane.
    /// </summary>
    private static bool WorkstationAccepts(IPlayer player, BaseContainer station, int slot, ItemStack item)
    {
        if (slot == WorkstationResultSlot(station))
            return false;

        return station switch
        {
            SmithingTable => SmithingAccepts(player, slot, item),
            Loom => slot switch
            {
                Loom.BannerSlot => TagsRegistry.Item.Banners.Entries.Contains(item.Holder.Id),
                Loom.DyeSlot => DyeOf(item) is not null,
                _ => BannerPatternsRegistry.IsPatternItem(item.Holder.Id)
            },
            Grindstone => GrindstoneAccepts(item),
            CartographyTable => slot == CartographyTable.MapSlot
                ? MapStorage.GetMapId(item) is not null
                : item.Is(ItemsRegistry.Paper) || item.Is(ItemsRegistry.Map) || item.Is(ItemsRegistry.GlassPane),
            _ => true
        };
    }

    /// <summary>
    /// Shift-clicks a stack in a workstation's window like vanilla's menus: from the workstation to the player's
    /// inventory, filling from the hotbar's last slot; from the player's inventory to the first input that takes it,
    /// else between the main inventory and the hotbar.
    /// </summary>
    private static bool MoveWorkstationItem(IPlayer player, BaseContainer station, BaseContainer source, int slot)
    {
        var inventory = player.Inventory;

        if (source != inventory)
            return MoveStack(player, station, slot, inventory, 9, 45, reverse: true);

        var item = inventory.GetItem(slot);
        if (item.IsNullOrAir())
            return false;

        var inputs = WorkstationResultSlot(station);
        for (var input = 0; input < inputs; input++)
        {
            if (WorkstationAccepts(player, station, input, item) && MoveStack(player, inventory, slot, station, input, input + 1, reverse: false))
                return true;
        }

        return slot < 36
            ? MoveStack(player, inventory, slot, inventory, 36, 45, reverse: false)
            : MoveStack(player, inventory, slot, inventory, 9, 36, reverse: false);
    }

    /// <summary>
    /// Handles the player clicking a button of a stonecutter (a recipe) or a loom (a pattern), like vanilla's
    /// <c>clickMenuButton</c>.
    /// </summary>
    internal static async ValueTask ClickWorkstationButtonAsync(IPlayer player, int containerId, int button)
    {
        var station = player.OpenedContainer;
        if (station is not (Stonecutter or Loom) || containerId != player.CurrentContainerId || !player.Alive)
            return;

        using (await BaseContainer.LockWindowAsync(station, player.Inventory))
        {
            if (!ReferenceEquals(player.OpenedContainer, station))
                return;

            switch (station)
            {
                case Stonecutter cutter when button >= 0
                    && button < StonecutterRecipes(player, cutter.GetItem(Stonecutter.InputSlot)).Count:
                    cutter.SelectedRecipe = button;
                    break;
                case Loom loom when button >= 0 && button < loom.SelectablePatterns.Length:
                    loom.SelectedPattern = button;
                    break;
                default:
                    return;
            }

            RefreshWorkstationResult(player, station);
        }

        await RefreshViewersAsync(station);
    }

    /// <summary>
    /// Gives the player back a workstation's inputs as its window closes, like vanilla's <c>clearContainer</c>: into
    /// their inventory, or dropped where they don't fit. The result goes, being only shown.
    /// </summary>
    internal static async ValueTask ReturnWorkstationItemsAsync(IPlayer player, BaseContainer station)
    {
        // Like vanilla's BeaconMenu.removed, a beacon's payment drops.
        if (station is BeaconContainer beacon)
        {
            SpawnThrownItem(player, beacon.GetItem(BeaconContainer.PaymentSlot) is { Count: > 0 } payment ? payment : null);
            beacon.RemoveItem(BeaconContainer.PaymentSlot);
            return;
        }

        var result = WorkstationResultSlot(station);
        for (var slot = 0; slot < station.Size; slot++)
        {
            if (slot == result || station.GetItem(slot) is not { Count: > 0 } item)
                continue;

            StoreCraftingItem(player.Inventory, item, 9, 45);
            SpawnThrownItem(player, item);
            station.RemoveItem(slot);
        }

        station.RemoveItem(result);
        await player.QueuePacketAsync(new ContainerSetContentPacket(0, player.Inventory.ToList()));
    }
}
