using Obsidian.Commands.Framework;
using Obsidian.API.Commands;
using Obsidian.API.Commands.ArgumentParsers;
using Obsidian.API.Inventory;
using Obsidian.API.Loot;
using Obsidian.API.World.Generator.RandomSources;
using System.Globalization;

namespace Obsidian.Commands.Modules;

[RequirePermissionLevel(2)]
public sealed class ItemCommandModule : CommandModuleBase
{
    [Command("item")]
    [CommandInfo("Changes items in slot sources", "/item <replace|fill|override|modify> <entity|block> …")]
    public async Task<int> ItemAsync([Remaining] string arguments)
    {
        try
        {
            var input = CommandInput.Arguments(this.CommandContext);
            var operation = input.Read();
            if (operation is not ("replace" or "fill" or "override" or "modify"))
                throw new FormatException("Unknown item operation");

            var targets = await CommandSlots.ReadAsync(this.CommandContext, input);
            if (targets.Count == 0)
                throw new FormatException("No applicable target slots");

            var loot = new LootContext { Random = new SystemRandomSource(Globals.Random) };
            var count = 0;
            if (operation == "modify")
            {
                var modifier = ReadModifier(input.Read());
                input.ExpectEnd();
                var visited = 0;
                foreach (var slot in targets)
                {
                    if (visited >= slot.Limit)
                        continue;

                    visited++;
                    var current = slot.Get() ?? ItemStack.Air;
                    var result = modifier.Apply(new ItemStack(current, current.Count), loot);
                    count += await ReplaceAsync(slot, result);
                }
            }
            else
            {
                var sourceKind = input.Read();
                var items = new List<ItemStack>();
                if (sourceKind == "with")
                {
                    var itemText = input.Read();
                    if (!new ItemArgumentParser().TryParseArgument(itemText, this.CommandContext, out var item))
                        throw new FormatException("Invalid item");

                    if (itemText.Contains('[') || itemText.Contains('{'))
                        throw new FormatException("Inline item components are not implemented");

                    var amount = input.End ? 1 : int.Parse(input.Read(), CultureInfo.InvariantCulture);
                    if (amount is < 1 or > 99)
                        throw new FormatException("Item count must be between 1 and 99");

                    items.Add(new ItemStack(item, amount));
                }
                else if (sourceKind == "from")
                {
                    var sources = await CommandSlots.ReadAsync(this.CommandContext, input);
                    // Snapshot every source before any write, including when source and target overlap.
                    items.AddRange(sources.Where(slot => slot.Visible).Select(slot => slot.Get() ?? ItemStack.Air)
                        .Select(stack => new ItemStack(stack, stack.Count)));
                    if (!input.End)
                    {
                        var modifier = ReadModifier(input.Read());
                        items = items.Select(item => modifier.Apply(item, loot)).ToList();
                    }
                }
                else
                {
                    throw new FormatException("Expected with or from");
                }

                input.ExpectEnd();
                count = await DistributeAsync(targets, items, operation);
            }

            if (count == 0)
                throw new FormatException("No slots changed");

            await this.CommandContext.SuccessAsync($"Changed {count} item slots");
            return count;
        }
        catch (Exception exception) when (ComputeCommandModule.IsInputError(exception))
        {
            await this.CommandContext.FailAsync(exception.Message);
            return 0;
        }
    }

    private static Obsidian.API.Loot.Functions.LootFunction ReadModifier(string text)
    {
        var data = CommandInput.Data(text);
        if (data.ValueKind == System.Text.Json.JsonValueKind.String)
            throw new FormatException("Registered loot modifiers are not implemented");

        return LootTableJson.ReadModifier(CommandInput.QualifyTypes(data).GetRawText());
    }

    internal static async Task<int> DistributeAsync(List<CommandSlots.Slot> targets, List<ItemStack> items, string operation)
    {
        var count = 0;
        var visited = 0;
        foreach (var group in targets.GroupBy(slot => slot.Owner, ReferenceEqualityComparer.Instance))
        {
            var index = 0;
            foreach (var target in group)
            {
                if (visited >= target.Limit)
                    continue;

                if (operation != "override" && (items.Count == 0 || operation == "replace" && index >= items.Count))
                    break;

                var exhausted = index >= items.Count;
                var source = operation == "override" && exhausted ? ItemStack.Air : items[index % items.Count];
                count += await ReplaceAsync(target, source);
                visited++;
                index++;
            }
        }

        return count;
    }

    private static async ValueTask<int> ReplaceAsync(CommandSlots.Slot slot, ItemStack item)
    {
        slot.Set(new ItemStack(item, item.Count));
        await slot.Sync();
        return 1;
    }
}
