using Obsidian.API.Commands;
using Obsidian.API.Inventory;
using Obsidian.API.Loot;
using Obsidian.Commands.Framework;
using Obsidian.Entities;
using Obsidian.Utilities;
using static Obsidian.Commands.CommandFeedback;

namespace Obsidian.Commands.Modules;

/// <summary>
/// Vanilla's commands that act on entities and players: <c>/kill</c>, <c>/summon</c>, <c>/enchant</c>,
/// <c>/clear</c> and <c>/tp</c>.
/// </summary>
[RequirePermissionLevel(2)]
public sealed class EntityCommandModule : CommandModuleBase
{
    // Vanilla's /give and /clear limit, a hundred stacks of 64.
    private const int MaxItemCount = 6400;

    [Command("kill")]
    [CommandInfo("Kills entities.", "/kill [<targets>]")]
    public async Task KillAsync()
    {
        if (this.CommandContext.Executor is not IEntity executor)
        {
            await this.CommandContext.FailAsync("Only entities can kill themselves; name the targets.");
            return;
        }

        await this.KillAsync([executor]);
    }

    [CommandOverload]
    public async Task KillAsync(IEntity[] targets)
    {
        if (await this.CommandContext.NoneFoundAsync(targets))
            return;

        foreach (var target in targets)
        {
            switch (target)
            {
                case PlayerBase player:
                    await player.KillAsync(player, ChatMessage.Simple($"{player.Username} was killed"));
                    break;
                case Living living:
                    await living.KillAsync(this.CommandContext.Executor ?? living);
                    break;
                default:
                    await target.RemoveAsync();
                    break;
            }
        }

        await this.CommandContext.SuccessAsync(targets.Length == 1
            ? $"Killed {NameOf(targets[0])}"
            : $"Killed {targets.Length} entities");
    }

    [Command("summon")]
    [CommandInfo("Summons an entity.", "/summon <entity> [<pos>]")]
    public Task SummonAsync(EntityType entity) => this.SummonAsync(entity, OriginOf(this.CommandContext));

    [CommandOverload]
    public async Task SummonAsync(EntityType entity, VectorD pos)
    {
        // Like vanilla's, /summon refuses entities that can't exist without an owner.
        if (entity is EntityType.Player or EntityType.FishingBobber)
        {
            await this.CommandContext.FailAsync("Unable to summon entity");
            return;
        }

        if (Math.Abs(pos.X) > 30_000_000 || Math.Abs(pos.Z) > 30_000_000 || pos.Y is < -20_000_000 or > 20_000_000)
        {
            await this.CommandContext.FailAsync("Invalid position for summon");
            return;
        }

        var level = LevelOf(this.CommandContext);
        var spawned = level.GetNewEntitySpawner()
            .WithEntityType(entity)
            .AtPosition(pos)
            .Spawn();

        await this.CommandContext.SuccessAsync($"Summoned new {NameOf(spawned)}");
    }

    [Command("enchant")]
    [CommandInfo("Enchants the item a target holds.", "/enchant <targets> <enchantment> [<level>]")]
    public Task EnchantAsync(IEntity[] targets, EnchantmentDefinition enchantment) => this.EnchantAsync(targets, enchantment, 1);

    [CommandOverload]
    public async Task EnchantAsync(IEntity[] targets, EnchantmentDefinition enchantment, int level)
    {
        if (await this.CommandContext.NoneFoundAsync(targets))
            return;

        if (level > enchantment.MaxLevel)
        {
            await this.CommandContext.FailAsync($"{level} is higher than the maximum level of {enchantment.MaxLevel} supported by that enchantment");
            return;
        }

        var enchanted = 0;
        foreach (var target in targets)
        {
            if (target is not PlayerBase player)
            {
                if (targets.Length == 1)
                    await this.CommandContext.FailAsync($"{NameOf(target)} is not a valid entity for this command");
                continue;
            }

            var slot = player.CurrentHeldItemSlot;
            if (player.Inventory.GetItem(slot) is not { IsAir: false } stack)
            {
                if (targets.Length == 1)
                    await this.CommandContext.FailAsync($"{player.Username} is not holding any item");
                continue;
            }

            var compatible = enchantment.CanEnchant(stack) && EnchantmentHelper.GetEnchantments(stack)
                .Where(existing => existing.Id != enchantment.Id)
                .All(existing => EnchantmentsRegistry.All.FirstOrDefault(x => x.Id == existing.Id) is not { } other
                    || EnchantmentDefinition.AreCompatible(enchantment, other));

            if (!compatible)
            {
                if (targets.Length == 1)
                    await this.CommandContext.FailAsync($"{stack.Holder.UnlocalizedName} cannot support that enchantment");
                continue;
            }

            EnchantmentHelper.Enchant(stack, enchantment, level);
            await player.SyncInventorySlotAsync(slot);
            enchanted++;
        }

        if (enchanted == 0)
        {
            if (targets.Length > 1)
                await this.CommandContext.FailAsync("No targets accepted that enchantment");
            return;
        }

        await this.CommandContext.SuccessAsync($"Applied enchantment {enchantment.Identifier} to {Describe(targets)}'s item");
    }

    [Command("give")]
    [CommandInfo("Gives an item to players.", "/give <targets> <item> [<count>]")]
    [RequirePermission(op: true, permissions: "obsidian.give")]
    public Task GiveAsync(IPlayer[] targets, Item item) => this.GiveAsync(targets, item, 1);

    [CommandOverload]
    public async Task GiveAsync(IPlayer[] targets, Item item, int count)
    {
        if (await this.CommandContext.NoneFoundAsync(targets))
            return;

        if (count is < 1 or > MaxItemCount)
        {
            await this.CommandContext.FailAsync($"Can't give more than {MaxItemCount} of {item.UnlocalizedName}");
            return;
        }

        foreach (var player in targets)
            await GiveItemAsync(player, item, count);

        await this.CommandContext.SuccessAsync($"Gave {count} [{item.UnlocalizedName}] to {Describe(targets, "players")}");
    }

    /// <summary>
    /// Gives a player stacks of an item, dropping what doesn't fit at their feet.
    /// </summary>
    internal static async Task GiveItemAsync(IPlayer player, Item item, int count)
    {
        var maxStack = Math.Max(1, new ItemStack(item).MaxStackSize);

        while (count > 0)
        {
            var stack = new ItemStack(item, Math.Min(count, maxStack));
            count -= stack.Count;

            PlayerBase.AddToInventory(player.Inventory, player.CurrentHeldItemSlot, stack);

            if (stack.Count > 0)
                player.Level.SpawnItem(player.Position, stack);
        }

        await SyncInventoryAsync(player);
    }

    [Command("clear")]
    [CommandInfo("Clears items from players' inventories.", "/clear [<targets>] [<item>] [<maxCount>]")]
    public Task ClearAsync() => this.Player is IPlayer player ? this.ClearAsync([player]) : this.CommandContext.FailAsync("A player is required to run this command here");

    [CommandOverload]
    public Task ClearAsync(IPlayer[] targets) => this.ClearItemsAsync(targets, null, -1);

    [CommandOverload]
    public Task ClearAsync(IPlayer[] targets, Item item) => this.ClearItemsAsync(targets, item, -1);

    [CommandOverload]
    public Task ClearAsync(IPlayer[] targets, Item item, int maxCount) => this.ClearItemsAsync(targets, item, maxCount);

    // Removes up to maxCount matching items (all with -1); a maxCount of 0 only counts them, as vanilla's does.
    private async Task ClearItemsAsync(IPlayer[] targets, Item? item, int maxCount)
    {
        if (await this.CommandContext.NoneFoundAsync(targets))
            return;

        var total = 0;
        foreach (var player in targets)
        {
            var remaining = maxCount < 0 ? int.MaxValue : maxCount;
            var found = 0;

            for (var slot = 0; slot < player.Inventory.Size; slot++)
            {
                if (player.Inventory.GetItem(slot) is not { IsAir: false } stack || item is not null && !stack.Is(item))
                    continue;

                if (maxCount == 0)
                {
                    found += stack.Count;
                    continue;
                }

                var taken = Math.Min(stack.Count, remaining);
                remaining -= taken;
                found += taken;

                player.Inventory.SetItem(slot, taken == stack.Count ? null : new ItemStack(stack, stack.Count - taken));

                if (remaining == 0)
                    break;
            }

            total += found;

            if (maxCount != 0)
                await SyncInventoryAsync(player);
        }

        if (total == 0)
        {
            await this.CommandContext.FailAsync(targets.Length == 1
                ? $"No items were found on player {targets[0].Username}"
                : $"No items were found on {targets.Length} players");
            return;
        }

        await this.CommandContext.SuccessAsync(maxCount == 0
            ? $"Found {total} matching item(s) on {Describe(targets, "players")}"
            : $"Removed {total} item(s) from {Describe(targets, "players")}");
    }

    [Command("gamemode")]
    [CommandInfo("Sets a player's game mode.", "/gamemode <gamemode> [<target>]")]
    [RequirePermission(op: true, permissions: "obsidian.gamemode")]
    public Task GameModeAsync(GameMode gamemode) =>
        this.Player is IPlayer player
            ? this.GameModeAsync(gamemode, [player])
            : this.CommandContext.FailAsync("A player is required to run this command here");

    [CommandOverload]
    public async Task GameModeAsync(GameMode gamemode, IPlayer[] targets)
    {
        if (await this.CommandContext.NoneFoundAsync(targets))
            return;

        var name = gamemode.ToString();
        foreach (var target in targets)
        {
            if (target.GameMode == gamemode)
                continue;

            await target.SetGamemodeAsync(gamemode);

            if (target != this.Player)
                await target.SendMessageAsync($"Your game mode has been updated to {name} Mode");
        }

        await this.CommandContext.SuccessAsync(targets is [var only] && only == this.Player
            ? $"Set own game mode to {name} Mode"
            : $"Set {Describe(targets, "players")}'s game mode to {name} Mode");
    }

    [Command("tp", "teleport")]
    [CommandInfo("Teleports entities.", "/tp [<targets>] <location|destination>")]
    public async Task TeleportAsync(VectorD location)
    {
        if (this.CommandContext.Executor is not IEntity executor)
            return;

        await this.TeleportAsync([executor], location);
    }

    [CommandOverload]
    public async Task TeleportAsync(IEntity destination)
    {
        if (this.CommandContext.Executor is not IEntity executor)
            return;

        await this.TeleportAsync([executor], destination);
    }

    [CommandOverload]
    public async Task TeleportAsync(IEntity[] targets, VectorD location)
    {
        if (await this.CommandContext.NoneFoundAsync(targets))
            return;

        var level = LevelOf(this.CommandContext);
        foreach (var target in targets)
        {
            if (target.Level == level)
                await target.TeleportAsync(location);
            else
                await target.TeleportAsync(level, location);
        }

        await this.CommandContext.SuccessAsync($"Teleported {Describe(targets, "entities")} to {Format(location)}");
    }

    [CommandOverload]
    public async Task TeleportAsync(IEntity[] targets, IEntity destination)
    {
        if (await this.CommandContext.NoneFoundAsync(targets))
            return;

        foreach (var target in targets)
        {
            if (target == destination)
                continue;

            if (target.Level == destination.Level)
                await target.TeleportAsync(destination.Position);
            else
                await target.TeleportAsync(destination.Level, destination.Position);
        }

        await this.CommandContext.SuccessAsync($"Teleported {Describe(targets, "entities")} to {NameOf(destination)}");
    }
}
